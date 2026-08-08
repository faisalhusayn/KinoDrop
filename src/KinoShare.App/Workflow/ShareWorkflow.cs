namespace KinoShare.App.Workflow;

using KinoShare.Core;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Core.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// The console interaction loop: ensure the workspace, create a temporary
/// SMB share over the single transfer folder, then present two actions while
/// the session is live - receive files (they appear live as they arrive) and
/// send files (copy them into the transfer folder so the phone can grab
/// them). Contains no Windows-specific logic.
/// </summary>
public sealed class ShareWorkflow
{
    private readonly ShareManager _shareManager;
    private readonly IWorkspaceService _workspaceService;
    private readonly INetworkService _networkService;
    private readonly ITransferMonitorService _transferMonitor;
    private readonly ILogger<ShareWorkflow> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareWorkflow"/> class.
    /// </summary>
    public ShareWorkflow(
        ShareManager shareManager,
        IWorkspaceService workspaceService,
        INetworkService networkService,
        ITransferMonitorService transferMonitor,
        ILogger<ShareWorkflow> logger)
    {
        _shareManager = shareManager ?? throw new ArgumentNullException(nameof(shareManager));
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
        _transferMonitor = transferMonitor ?? throw new ArgumentNullException(nameof(transferMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the workflow and returns the process exit code.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the workflow.</param>
    /// <returns><c>0</c> on success, <c>1</c> on failure, <c>130</c> on cancel.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        WriteBanner();

        ShareSession? session = null;
        bool removed = false;

        try
        {
            WorkspaceInfo workspace = await _workspaceService.EnsureCreatedAsync(cancellationToken);

            var request = new ShareRequest(workspace.TransferFolderPath, KinoShareDefaults.ShareName);
            session = await _shareManager.CreateShareSessionAsync(request, cancellationToken);

            PrintConnectionDetails(session);

            _transferMonitor.FileReceived += OnFileReceived;
            _transferMonitor.FileSent += OnFileSent;
            _transferMonitor.Start(workspace.TransferFolderPath);

            PrintCommands();
            await RunCommandLoopAsync(workspace, cancellationToken);

            _logger.LogInformation("User requested share removal.");
            await _shareManager.RemoveShareSessionAsync(session, cancellationToken);
            removed = true;

            Console.WriteLine();
            Console.WriteLine("Share and temporary user removed. Bye!");
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Workflow cancelled.");
            return 130;
        }
        catch (KinoShareException exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
        finally
        {
            _transferMonitor.FileReceived -= OnFileReceived;
            _transferMonitor.FileSent -= OnFileSent;
            _transferMonitor.Stop();

            if (session is not null && !removed)
            {
                try
                {
                    await _shareManager.RemoveShareSessionAsync(session, CancellationToken.None);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Failed to clean up share session {ShareName}.", session.Share.Name);
                }
            }
        }
    }

    private async Task RunCommandLoopAsync(WorkspaceInfo workspace, CancellationToken cancellationToken)
    {
        while (true)
        {
            string? input = await Console.In.ReadLineAsync(cancellationToken);

            if (input is null)
            {
                return;
            }

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts[0].ToLowerInvariant())
            {
                case "quit" or "exit" or "stop":
                    return;

                case "files" or "receive":
                    ListFiles(workspace);
                    break;

                case "send":
                    SendFiles(workspace, parts.Skip(1).ToArray());
                    break;

                default:
                    Console.WriteLine($"Unknown command '{parts[0]}'.");
                    PrintCommands();
                    break;
            }
        }
    }

    private void SendFiles(WorkspaceInfo workspace, string[] paths)
    {
        if (paths.Length == 0)
        {
            Console.WriteLine("Usage: send <path> [<path> ...]");
            return;
        }

        foreach (string rawPath in paths)
        {
            if (!File.Exists(rawPath))
            {
                Console.Error.WriteLine($"  '{rawPath}' does not exist or is not a file.");
                continue;
            }

            string fileName = Path.GetFileName(rawPath);
            string destination = Path.Combine(workspace.TransferFolderPath, fileName);

            if (File.Exists(destination))
            {
                Console.Error.WriteLine($"  '{fileName}' already exists in the transfer folder; move or rename it first.");
                continue;
            }

            try
            {
                File.Copy(rawPath, destination);
                _transferMonitor.RegisterAppCopiedFile(fileName);
                Console.WriteLine($"  '{fileName}' copied to the transfer folder - visible on the phone now.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"  Failed to copy '{fileName}': {exception.Message}");
            }
        }
    }

    private static void ListFiles(WorkspaceInfo workspace)
    {
        Console.WriteLine();
        Console.WriteLine($"Transfer folder -> {workspace.TransferFolderPath}");
        PrintFolderContents(workspace.TransferFolderPath);
    }

    private static void PrintFolderContents(string folderPath)
    {
        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(folderPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"  Cannot list folder: {exception.Message}");
            return;
        }

        foreach (string file in files)
        {
            var info = new FileInfo(file);
            Console.WriteLine($"  {info.Name} ({FormatSize(info.Length)})");
        }
    }

    private static string FormatSize(long bytes)
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

    private void OnFileReceived(object? sender, FileTransferredEventArgs e)
    {
        Console.WriteLine();
        Console.WriteLine($"[Received] {e.FileName} ({FormatSize(e.Size)}) - from phone, saved to the transfer folder.");
    }

    private void OnFileSent(object? sender, FileTransferredEventArgs e)
    {
        Console.WriteLine();
        Console.WriteLine($"[Sent] {e.FileName} ({FormatSize(e.Size)}) - ready in the transfer folder for the phone.");
    }

    private void PrintConnectionDetails(ShareSession session)
    {
        ShareInfo share = session.Share;
        // An IP address is preferred over the machine name: name resolution
        // often fails on phone-hotspot networks.
        string host = _networkService.GetPrimaryPrivateIpAddressV4() ?? Environment.MachineName;

        Console.WriteLine();
        Console.WriteLine("Share created successfully.");
        Console.WriteLine($"  SMB path   : smb://{host}/{share.Name}");
        Console.WriteLine();
        Console.WriteLine("Connect from your iPhone with:");
        Console.WriteLine($"  Files app -> Connect to Server -> smb://{host}/{share.Name}");
        Console.WriteLine("  Choose 'Registered User' (NOT Guest) and enter:");
        Console.WriteLine($"    Username : {session.User.Username}");
        Console.WriteLine($"    Password : {session.User.Password}");
        Console.WriteLine();
        Console.WriteLine("  Everything the phone copies into the folder lands here and appears live.");
        Console.WriteLine("  Files you 'send' below land in the same folder and are visible on the phone.");
        Console.WriteLine("  Tip: if the phone says 'Content Unavailable', you connected as Guest.");
    }

    private static void PrintCommands()
    {
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  files              list the transfer folder");
        Console.WriteLine("  send <path> [...]   copy file(s) into the transfer folder for the phone");
        Console.WriteLine("  quit               remove the share and exit");
        Console.WriteLine();
    }

    private static void WriteBanner()
    {
        Console.WriteLine("KinoShare - file transfer between this PC and your iPhone.");
        Console.WriteLine("Uses Windows' built-in SMB server; nothing is installed on the phone.");
    }
}
