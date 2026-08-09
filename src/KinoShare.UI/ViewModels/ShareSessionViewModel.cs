namespace KinoShare.UI.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using KinoShare.Core;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Core.Services;
using KinoShare.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

/// <summary>The lifecycle state of the sharing session.</summary>
public enum SessionStatus
{
    Idle,
    Starting,
    Running,
    Stopped,
    Failed,
}

/// <summary>
/// The single-window view model: starts and stops one sharing session over a
/// user-chosen transfer folder, mirrors the live transfer feed, and exposes
/// commands for sending files, changing the folder, and copying credentials.
/// </summary>
public sealed class ShareSessionViewModel : INotifyPropertyChanged
{
    private readonly Func<string?, WorkspaceService> _workspaceFactory;
    private readonly IAppSettingsService _settingsService;
    private readonly ShareManager _shareManager;
    private readonly INetworkService _networkService;
    private readonly ITransferMonitorService _transferMonitor;
    private readonly ITransferHistoryService _historyService;
    private readonly IToastService _toastService;
    private readonly IFirewallService _firewallService;
    private readonly IDeviceCredentialStore _credentialStore;
    private readonly ILogger<ShareSessionViewModel> _logger;
    private readonly DispatcherQueue _dispatcher;

    private WorkspaceService? _workspace;
    private ShareSession? _session;
    private SessionStatus _status;
    private bool _isBusy;
    private string? _smbPath;
    private string? _username;
    private string? _password;
    private string? _transferFolderPath;
    private string? _infoMessage;
    private InfoBarSeverity _infoSeverity;
    private bool _infoOpen;
    private bool _isDarkTheme;
    private SoftwareBitmapSource? _qrCodeSource;
    private bool _showQr;
    private readonly Dictionary<string, ActiveTransferEntry> _activeByName = new(StringComparer.OrdinalIgnoreCase);

    private const int MinPasswordLength = 6;
    private const int MaxPasswordLength = 64;

    public ShareSessionViewModel(
        Func<string?, WorkspaceService> workspaceFactory,
        IAppSettingsService settingsService,
        ShareManager shareManager,
        INetworkService networkService,
        ITransferMonitorService transferMonitor,
        ITransferHistoryService historyService,
        IToastService toastService,
        IFirewallService firewallService,
        IDeviceCredentialStore credentialStore,
        ILogger<ShareSessionViewModel> logger,
        DispatcherQueue dispatcher)
    {
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _shareManager = shareManager ?? throw new ArgumentNullException(nameof(shareManager));
        _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
        _transferMonitor = transferMonitor ?? throw new ArgumentNullException(nameof(transferMonitor));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
        _firewallService = firewallService ?? throw new ArgumentNullException(nameof(firewallService));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        StartSessionCommand = new RelayCommand(async () => await StartSessionAsync(), () => !_isBusy);
        StopSessionCommand = new RelayCommand(async () => await StopSessionAsync(), () => _status == SessionStatus.Running && !_isBusy);
        ThemeToggleCommand = new RelayCommand(ToggleThemeAsync);

        _ = InitializeThemeAsync();
        _ = LoadTransferFolderPathAsync();
        _ = LoadHistoryAsync();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the live feed of completed transfers.</summary>
    public ObservableCollection<TransferEntry> Transfers { get; } = [];

    /// <summary>Gets the transfers that are still being received or sent.</summary>
    public ObservableCollection<ActiveTransferEntry> ActiveTransfers { get; } = [];

    /// <summary>Gets the current contents of the transfer folder.</summary>
    public ObservableCollection<FileEntry> FolderFiles { get; } = [];

    /// <summary>Gets whether any transfer is still in progress.</summary>
    public bool HasActiveTransfers => ActiveTransfers.Count > 0;

    /// <summary>Gets the command that starts a sharing session.</summary>
    public ICommand StartSessionCommand { get; }

    /// <summary>Gets the command that stops the current session.</summary>
    public ICommand StopSessionCommand { get; }

    /// <summary>Gets the command that toggles the light/dark theme.</summary>
    public ICommand ThemeToggleCommand { get; }

    /// <summary>Gets whether a session is currently active.</summary>
    public bool IsRunning => _status == SessionStatus.Running;

    /// <summary>Gets the current session status.</summary>
    public SessionStatus Status => _status;

    /// <summary>Gets a user-facing status label.</summary>
    public string StatusLabel => _status switch
    {
        SessionStatus.Starting => "Starting…",
        SessionStatus.Running => "Running",
        SessionStatus.Stopped => "Stopped",
        SessionStatus.Failed => "Failed",
        _ => "Idle",
    };

    /// <summary>Gets whether the session is currently provisioning.</summary>
    public bool IsStarting => _status == SessionStatus.Starting;

    /// <summary>Gets the SMB path clients connect to.</summary>
    public string? SmbPath => _smbPath;

    /// <summary>Gets the temporary username.</summary>
    public string? Username => _username;

    /// <summary>Gets the temporary password.</summary>
    public string? Password => _password;

    /// <summary>Gets whether the connection password can be changed right now.</summary>
    public bool CanEditPassword => !IsRunning;

    /// <summary>Gets the transfer folder path.</summary>
    public string? TransferFolderPath => _transferFolderPath;

    /// <summary>Gets the InfoBar message, or null when hidden.</summary>
    public string? InfoMessage => _infoMessage;

    /// <summary>Gets the InfoBar severity.</summary>
    public InfoBarSeverity InfoSeverity => _infoSeverity;

    /// <summary>Gets whether the InfoBar is visible.</summary>
    public bool IsInfoOpen => _infoOpen;

    /// <summary>Gets whether the dark theme is active.</summary>
    public bool IsDarkTheme => _isDarkTheme;

    /// <summary>Gets the QR code image for the current session, or null.</summary>
    public SoftwareBitmapSource? QrCodeSource => _qrCodeSource;

    /// <summary>Gets whether the QR code is visible.</summary>
    public bool ShowQr => _showQr;

    /// <summary>Starts the sharing session and returns a user-facing error or null.</summary>
    public async Task<string?> StartSessionAsync()
    {
        if (_status is SessionStatus.Starting or SessionStatus.Running || _isBusy)
        {
            return null;
        }

        _isBusy = true;
        _status = SessionStatus.Starting;
        UpdateState();

        try
        {
            string? location = await _settingsService.GetTransferFolderLocationAsync();
            _workspace = _workspaceFactory(location);
            WorkspaceInfo workspace = await _workspace.EnsureCreatedAsync();

            var request = new ShareRequest(workspace.TransferFolderPath, KinoShareDefaults.ShareName);
            ShareSession session = await _shareManager.CreateShareSessionAsync(request);

            _session = session;
            SetFolderPath(workspace.TransferFolderPath);
            SetCredentials(session);
            RefreshQrCode(BuildQrPayload());

            _transferMonitor.FileReceived += OnFileReceived;
            _transferMonitor.FileSent += OnFileSent;
            _transferMonitor.FileProgress += OnFileProgress;
            _transferMonitor.Start(workspace.TransferFolderPath);

            _status = SessionStatus.Running;
            ActiveTransfers.Clear();
            _activeByName.Clear();
            RefreshFolderContents(workspace.TransferFolderPath);
            ShowInfo(InfoBarSeverity.Success, "Sharing session is live. Connect from your iPhone and enter the credentials below.");
            UpdateState();

            _ = VerifyFirewallAsync();
            return null;
        }
        catch (KinoShareException exception)
        {
            _logger.LogError(exception, "Failed to start the sharing session.");
            _status = SessionStatus.Failed;
            ShowInfo(InfoBarSeverity.Error, $"Could not start the session: {exception.Message}");
            UpdateState();
            return exception.Message;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected failure while starting the sharing session.");
            _status = SessionStatus.Failed;
            ShowInfo(InfoBarSeverity.Error, "An unexpected error occurred while starting the session.");
            UpdateState();
            return "An unexpected error occurred while starting the session.";
        }
        finally
        {
            _isBusy = false;
            UpdateState();
        }
    }

    /// <summary>Stops the current session and returns a user-facing error or null.</summary>
    public async Task<string?> StopSessionAsync()
    {
        if (_status != SessionStatus.Running || _isBusy)
        {
            return null;
        }

        _isBusy = true;
        UpdateState();

        try
        {
            if (_session is not null)
            {
                await _shareManager.RemoveShareSessionAsync(_session);
            }

            _transferMonitor.FileReceived -= OnFileReceived;
            _transferMonitor.FileSent -= OnFileSent;
            _transferMonitor.FileProgress -= OnFileProgress;
            _transferMonitor.Stop();

            _session = null;
            _status = SessionStatus.Stopped;
            ClearQrCode();
            ShowInfo(InfoBarSeverity.Informational, "Session stopped. The share and temporary user were removed.");
            UpdateState();
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to stop the sharing session.");
            _status = SessionStatus.Failed;
            ShowInfo(InfoBarSeverity.Error, $"Could not stop the session: {exception.Message}");
            UpdateState();
            return exception.Message;
        }
        finally
        {
            _isBusy = false;
            UpdateState();
        }
    }

    /// <summary>
    /// Saves a user-chosen connection password for all future sessions.
    /// Returns a user-facing error, or null on success.
    /// </summary>
    public async Task<string?> ChangePasswordAsync(string newPassword)
    {
        if (IsRunning)
        {
            return "Stop the session before changing the connection password.";
        }

        if (newPassword.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            return $"The password must be {MinPasswordLength}-{MaxPasswordLength} characters.";
        }

        try
        {
            await _credentialStore.SetPasswordAsync(newPassword);
            _password = newPassword;
            OnPropertyChanged(nameof(Password));
            ShowInfo(
                InfoBarSeverity.Success,
                "Connection password saved. Devices enter it once; after that Files remembers it.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save the connection password.");
            ShowInfo(InfoBarSeverity.Error, $"Could not save the password: {exception.Message}");
            return $"Could not save the password: {exception.Message}";
        }
    }

    /// <summary>Changes the transfer folder location for the next session.</summary>
    public async Task<string?> ChangeFolderAsync(string location)
    {
        if (_status is SessionStatus.Starting or SessionStatus.Running)
        {
            return "Stop the session before changing the transfer folder.";
        }

        try
        {
            string resolved = WorkspaceService.ResolveTransferFolder(location);
            await _settingsService.SetTransferFolderLocationAsync(location);
            _transferFolderPath = resolved;
            OnPropertyChanged(nameof(TransferFolderPath));
            ShowInfo(InfoBarSeverity.Informational, $"Files will be received into {resolved} from now on.");
            UpdateState();
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to change the transfer folder.");
            ShowInfo(InfoBarSeverity.Error, $"Could not change the folder: {exception.Message}");
            return $"Could not change the folder: {exception.Message}";
        }
    }

    /// <summary>Sends a file into the transfer folder for the phone.</summary>
    public async Task<string?> SendFileAsync(string path)
    {
        if (!IsRunning || _workspace is null || _transferFolderPath is null)
        {
            return "Start the session before sending files.";
        }

        if (!File.Exists(path))
        {
            return $"'{path}' does not exist or is not a file.";
        }

        try
        {
            string fileName = Path.GetFileName(path);
            string destination = Path.Combine(_transferFolderPath, fileName);

            if (File.Exists(destination))
            {
                return $"'{fileName}' already exists in the transfer folder; move or rename it first.";
            }

            await Task.Run(() => File.Copy(path, destination));
            _transferMonitor.RegisterAppCopiedFile(fileName);
            RefreshFolderContents(_transferFolderPath);
            ShowInfo(InfoBarSeverity.Success, $"'{fileName}' copied to the transfer folder — visible on the phone now.");
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Failed to send '{Path}'.", path);
            ShowInfo(InfoBarSeverity.Error, $"Failed to copy '{Path.GetFileName(path)}': {exception.Message}");
            return $"Failed to copy '{Path.GetFileName(path)}': {exception.Message}";
        }
    }

    /// <summary>Refreshes the folder contents listing.</summary>
    public void RefreshFolderContents()
    {
        if (_transferFolderPath is not null)
        {
            RefreshFolderContents(_transferFolderPath);
        }
    }

    /// <summary>Opens the transfer folder in File Explorer.</summary>
    public void OpenTransferFolder()
    {
        if (_transferFolderPath is null || !Directory.Exists(_transferFolderPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _transferFolderPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open the transfer folder {Folder}.", _transferFolderPath);
        }
    }

    /// <summary>Dismisses the InfoBar.</summary>
    public void DismissInfo()
    {
        _infoOpen = false;
        OnPropertyChanged(nameof(IsInfoOpen));
    }

    /// <summary>Switches the UI between light and dark theme.</summary>
    public async Task ToggleThemeAsync()
    {
        bool dark = !_isDarkTheme;
        _isDarkTheme = dark;
        OnPropertyChanged(nameof(IsDarkTheme));

        try
        {
            await _settingsService.SetThemeAsync(dark ? "Dark" : "Light");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not persist the theme choice.");
        }
    }

    private async Task InitializeThemeAsync()
    {
        try
        {
            string? theme = await _settingsService.GetThemeAsync();
            _isDarkTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load the theme choice.");
        }

        OnPropertyChanged(nameof(IsDarkTheme));
    }

    private async Task VerifyFirewallAsync()
    {
        try
        {
            if (!await _firewallService.IsSmbInboundAllowedAsync())
            {
                MarshalToUi(() => ShowInfo(
                    InfoBarSeverity.Warning,
                    "Windows Firewall may block the share on this network. Allow 'File and Printer Sharing' " +
                    "on the active network profile if the phone cannot connect."));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not verify the SMB firewall rule.");
        }
    }

    private async Task LoadTransferFolderPathAsync()
    {
        try
        {
            string? location = await _settingsService.GetTransferFolderLocationAsync();
            _transferFolderPath = WorkspaceService.ResolveTransferFolder(location);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load the transfer folder location.");
        }

        OnPropertyChanged(nameof(TransferFolderPath));
    }

    private void SetFolderPath(string path)
    {
        _transferFolderPath = path;
        OnPropertyChanged(nameof(TransferFolderPath));
    }

    private void SetCredentials(ShareSession session)
    {
        string host = _networkService.GetPrimaryPrivateIpAddressV4() ?? Environment.MachineName;
        _smbPath = $"smb://{host}/{session.Share.Name}";
        _username = session.User.Username;
        _password = session.User.Password;
        OnPropertyChanged(nameof(SmbPath));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Password));
    }

    private string BuildQrPayload()
    {
        if (string.IsNullOrWhiteSpace(_smbPath)
            || string.IsNullOrWhiteSpace(_username)
            || string.IsNullOrWhiteSpace(_password))
        {
            return _smbPath ?? string.Empty;
        }

        return $"{_smbPath}?user={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}&name={Uri.EscapeDataString(Environment.MachineName)}";
    }

    /// <summary>Shows and regenerates the connection QR code for the payload.</summary>
    private void RefreshQrCode(string? payload)
    {
        _showQr = true;
        OnPropertyChanged(nameof(ShowQr));

        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var generator = new QRCodeGenerator();
                QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(data);
                byte[] png = qrCode.GetGraphic(8);
                MarshalToUi(() => SetQrCodeFromPng(png));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to generate the QR code.");
            }
        });
    }

    /// <summary>Displays the generated QR PNG as an image source.</summary>
    private async void SetQrCodeFromPng(byte[] png)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);

            _qrCodeSource = source;
            OnPropertyChanged(nameof(QrCodeSource));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to display the QR code.");
        }
    }

    /// <summary>Hides and clears the QR code when the session ends.</summary>
    private void ClearQrCode()
    {
        _qrCodeSource = null;
        _showQr = false;
        OnPropertyChanged(nameof(QrCodeSource));
        OnPropertyChanged(nameof(ShowQr));
    }

    private void OnFileReceived(object? sender, FileTransferredEventArgs e)
        => MarshalToUi(() =>
        {
            CompleteActive(e.FileName, e.Size);
            RemovePartialEntries(e.FileName);
            AddTransfer("Received", e.FileName, e.Size, e.Timestamp);
            RefreshFolderContents();
        });

    private void OnFileSent(object? sender, FileTransferredEventArgs e)
        => MarshalToUi(() =>
        {
            CompleteActive(e.FileName, e.Size);
            RemovePartialEntries(e.FileName);
            AddTransfer("Sent", e.FileName, e.Size, e.Timestamp);
            RefreshFolderContents();
        });

    /// <summary>Adds a transfer to the live feed, persists it, and notifies.</summary>
    private void AddTransfer(string direction, string fileName, long size, DateTime timestamp)
    {
        Transfers.Insert(0, new TransferEntry(direction, fileName, size, timestamp));
        _toastService.ShowTransferCompleted(direction, fileName, TransferEntry.FormatSize(size));
        _ = PersistTransferAsync(direction, fileName, size, timestamp);
    }

    private async Task PersistTransferAsync(string direction, string fileName, long size, DateTime timestamp)
    {
        try
        {
            await _historyService.AddAsync(new TransferRecord(direction, fileName, size, timestamp));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist transfer history for '{FileName}'.", fileName);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            IReadOnlyList<TransferRecord> records = await _historyService.LoadAsync();
            MarshalToUi(() =>
            {
                foreach (TransferRecord record in records)
                {
                    Transfers.Add(new TransferEntry(record.Direction, record.FileName, record.Size, record.Timestamp));
                }
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load transfer history.");
        }
    }

    private void OnFileProgress(object? sender, FileProgressEventArgs e)
        => MarshalToUi(() =>
        {
            if (_activeByName.TryGetValue(e.FileName, out ActiveTransferEntry? entry))
            {
                entry.Update(e.BytesCopied, e.Timestamp, e.TotalBytes);
            }
            else
            {
                entry = new ActiveTransferEntry(e.FileName, e.BytesCopied, e.IsAppCopy, e.TotalBytes);
                _activeByName[e.FileName] = entry;
                ActiveTransfers.Insert(0, entry);
                OnPropertyChanged(nameof(HasActiveTransfers));
            }
        });

    /// <summary>Snaps the in-progress row to 100%, then removes it shortly after.</summary>
    private void CompleteActive(string fileName, long finalSize)
    {
        if (_activeByName.TryGetValue(fileName, out ActiveTransferEntry? entry))
        {
            entry.Complete(finalSize);
            _ = Task.Delay(800).ContinueWith(_ => MarshalToUi(() => RemoveActive(fileName)));
        }
    }

    private void RemoveActive(string fileName)
    {
        if (_activeByName.TryGetValue(fileName, out ActiveTransferEntry? entry))
        {
            _activeByName.Remove(fileName);
            ActiveTransfers.Remove(entry);
            OnPropertyChanged(nameof(HasActiveTransfers));
        }
    }

    private void RemovePartialEntries(string finalFileName)
    {
        string partialPrefix = $"{finalFileName}.kinodrop-";
        foreach (ActiveTransferEntry entry in ActiveTransfers
            .Where(entry => entry.FileName.StartsWith(partialPrefix, StringComparison.OrdinalIgnoreCase)
                && entry.FileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            RemoveActive(entry.FileName);
        }
    }

    private void MarshalToUi(Action action)
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    private void RefreshFolderContents(string folderPath)
    {
        try
        {
            List<FileInfo> files = Directory.EnumerateFiles(folderPath)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTime)
                .ToList();

            FolderFiles.Clear();
            foreach (FileInfo info in files)
            {
                FolderFiles.Add(new FileEntry(info.Name, info.Length, info.LastWriteTime));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not list the transfer folder {Folder}.", folderPath);
        }
    }

    private void ShowInfo(InfoBarSeverity severity, string message)
    {
        _infoSeverity = severity;
        _infoMessage = message;
        _infoOpen = true;
        OnPropertyChanged(nameof(InfoSeverity));
        OnPropertyChanged(nameof(InfoMessage));
        OnPropertyChanged(nameof(IsInfoOpen));
    }

    private void UpdateState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(CanEditPassword));
        ((RelayCommand)StartSessionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopSessionCommand).RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Describes a single completed transfer shown in the live feed.</summary>
public sealed record TransferEntry(string Direction, string FileName, long Size, DateTime Timestamp)
{
    /// <summary>Gets whether this transfer came from the phone.</summary>
    public bool IsReceived => Direction == "Received";

    /// <summary>Gets the time the transfer completed (HH:mm).</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>Gets a display-friendly size text.</summary>
    public string SizeText => FormatSize(Size);

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}

/// <summary>Describes a file currently in the transfer folder.</summary>
public sealed record FileEntry(string Name, long Size, DateTime LastWriteTime)
{
    /// <summary>Gets a display-friendly size text.</summary>
    public string SizeText => TransferEntry.FormatSize(Size);

    /// <summary>Gets the modified time (dd MMM HH:mm).</summary>
    public string ModifiedText => LastWriteTime.ToString("dd MMM HH:mm");
}

/// <summary>Describes a transfer that is still being received or sent.</summary>
public sealed class ActiveTransferEntry : INotifyPropertyChanged
{
    private const double SpeedSmoothing = 0.3;
    private long _bytesCopied;
    private long? _totalBytes;
    private long _lastBytes;
    private DateTime _lastSample;
    private double _speedBytesPerSecond;
    private bool _completed;

    public ActiveTransferEntry(string fileName, long bytesCopied, bool isAppCopy, long? totalBytes = null)
    {
        FileName = fileName;
        _bytesCopied = bytesCopied;
        _lastBytes = bytesCopied;
        _lastSample = DateTime.Now;
        IsAppCopy = isAppCopy;
        _totalBytes = totalBytes;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the file name.</summary>
    public string FileName { get; }

    /// <summary>Gets whether this transfer is a send (the app placed the file).</summary>
    public bool IsAppCopy { get; }

    /// <summary>Gets whether this transfer came from the phone.</summary>
    public bool IsReceived => !IsAppCopy;

    /// <summary>Gets the bytes observed so far.</summary>
    public long BytesCopied => _bytesCopied;

    /// <summary>Gets a display-friendly size text for the bytes so far.</summary>
    public string SizeText => TransferEntry.FormatSize(_bytesCopied);

    /// <summary>Gets the current transfer speed.</summary>
    public string SpeedText => FormatSpeed(_speedBytesPerSecond);

    /// <summary>Gets the size so far and the current speed, e.g. "412 MB · 12.4 MB/s".</summary>
    public string StatsText => $"{SizeText} · {SpeedText}";

    /// <summary>
    /// Gets the determinate progress value when the sender published a total.
    /// </summary>
    public double ProgressValue => _totalBytes is > 0 ? Math.Min(_bytesCopied, _totalBytes.Value) : 0;

    /// <summary>Gets the expected total, or zero when it is unknown.</summary>
    public double ProgressMaximum => _totalBytes is > 0 ? _totalBytes.Value : 0;

    /// <summary>Gets whether the expected total is unavailable.</summary>
    public bool IsProgressIndeterminate => _totalBytes is not > 0 && !_completed;

    /// <summary>Updates the bytes observed so far and the transfer speed.</summary>
    public void Update(long bytesCopied, DateTime timestamp, long? totalBytes = null)
    {
        if (bytesCopied == _bytesCopied)
        {
            return;
        }

        double elapsedSeconds = (timestamp - _lastSample).TotalSeconds;
        if (elapsedSeconds > 0)
        {
            long delta = bytesCopied - _lastBytes;
            if (delta > 0)
            {
                double instant = delta / elapsedSeconds;
                _speedBytesPerSecond = _speedBytesPerSecond <= 0
                    ? instant
                    : (instant * SpeedSmoothing) + (_speedBytesPerSecond * (1 - SpeedSmoothing));
            }
        }

        _lastBytes = bytesCopied;
        _lastSample = timestamp;
        _bytesCopied = bytesCopied;
        if (totalBytes is > 0)
        {
            _totalBytes = totalBytes;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatsText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressValue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressMaximum)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProgressIndeterminate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesCopied)));
    }

    /// <summary>
    /// Marks the transfer as finished: with the final size now known, the bar
    /// fills to exactly 100% before the row moves to the completed feed.
    /// </summary>
    public void Complete(long finalBytes)
    {
        if (finalBytes <= 0)
        {
            return;
        }

        _completed = true;
        _speedBytesPerSecond = 0;
        _bytesCopied = finalBytes;
        _totalBytes = finalBytes;
        _lastBytes = finalBytes;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpeedText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatsText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressValue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressMaximum)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProgressIndeterminate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesCopied)));
    }

    internal static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0} KB/s";
        }

        return $"{bytesPerSecond:0} B/s";
    }
}

/// <summary>A minimal <see cref="ICommand"/> implementation.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public async void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            await _execute();
        }
    }

    /// <summary>Raises <see cref="CanExecuteChanged"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
