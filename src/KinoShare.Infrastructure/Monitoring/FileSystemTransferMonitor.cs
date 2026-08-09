namespace KinoShare.Infrastructure.Monitoring;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Raises transfer events by polling the single transfer folder. A file only
/// counts as transferred once its size has stayed unchanged across consecutive
/// scans, so partially copied files (e.g. mid-SMB-transfer) do not produce
/// events prematurely. Files present when the monitor starts are seeded and
/// never reported; files the app placed itself (registered via
/// <see cref="RegisterAppCopiedFile"/>) are reported as sent.
///
/// Polling is used instead of <see cref="System.IO.FileSystemWatcher"/>
/// because change notifications proved unreliable in sandboxed/restricted
/// environments; polling also naturally matches how network copies arrive.
/// </summary>
public sealed class FileSystemTransferMonitor : ITransferMonitorService
{
    private const string MetadataSuffix = ".kinodrop-meta";
    private readonly ILogger<FileSystemTransferMonitor> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly int _stableSamples;

    private readonly Dictionary<string, FileState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _appCopied = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _pollLoop;
    private string _transferFolderPath = string.Empty;
    private bool _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTransferMonitor"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="pollInterval">Delay between directory scans.</param>
    /// <param name="stableSamples">Consecutive identical size scans required before a file is reported.</param>
    public FileSystemTransferMonitor(
        ILogger<FileSystemTransferMonitor> logger,
        TimeSpan? pollInterval = null,
        int stableSamples = 2)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _stableSamples = stableSamples < 1 ? 1 : stableSamples;
    }

    /// <inheritdoc />
    public event EventHandler<FileTransferredEventArgs>? FileReceived;

    /// <inheritdoc />
    public event EventHandler<FileTransferredEventArgs>? FileSent;

    /// <inheritdoc />
    public event EventHandler<FileProgressEventArgs>? FileProgress;

    /// <inheritdoc />
    public void Start(string transferFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transferFolderPath);

        if (!Directory.Exists(transferFolderPath))
        {
            throw new DirectoryNotFoundException($"The transfer folder '{transferFolderPath}' does not exist.");
        }

        if (_running)
        {
            return;
        }

        _transferFolderPath = transferFolderPath;

        // Seed files that already exist so they are never reported as new
        // transfers when the session starts.
        foreach (string file in Directory.EnumerateFiles(_transferFolderPath))
        {
            _reported.Add(Path.GetFileName(file));
        }

        _cts = new CancellationTokenSource();
        _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
        _running = true;

        _logger.LogInformation("Transfer monitor started for folder {Path}.", transferFolderPath);
    }

    /// <inheritdoc />
    public void RegisterAppCopiedFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _appCopied.Add(Path.GetFileName(fileName));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _pollLoop = null;

        _states.Clear();
        _reported.Clear();
        _appCopied.Clear();

        _logger.LogInformation("Transfer monitor stopped.");
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, cancellationToken);

            try
            {
                ScanDirectory(_transferFolderPath);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to scan transfer folder {Path}.", _transferFolderPath);
            }
        }
    }

    private void ScanDirectory(string folderPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> expectedSizes = ReadExpectedSizes(folderPath);

        foreach (string fullPath in Directory.EnumerateFiles(folderPath))
        {
            string name = Path.GetFileName(fullPath);

            if (name.EndsWith(MetadataSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_reported.Contains(name))
            {
                seen.Add(name);
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(fullPath).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            seen.Add(name);

            if (!_states.TryGetValue(name, out FileState? state))
            {
                _states[name] = new FileState(size);

                // First sighting: a new file is arriving, so report progress
                // with the bytes observed so far.
                FileProgress?.Invoke(this, new FileProgressEventArgs(
                    name, fullPath, size, _appCopied.Contains(name), DateTime.Now,
                    expectedSizes.GetValueOrDefault(name)));
                continue;
            }

            // The file is still being tracked: it has not stabilized yet, so
            // report how much of it has arrived.
            FileProgress?.Invoke(this, new FileProgressEventArgs(
                name, fullPath, size, _appCopied.Contains(name), DateTime.Now,
                expectedSizes.GetValueOrDefault(name)));

            if (state.Size == size)
            {
                state.StableSamples++;

                if (state.StableSamples >= _stableSamples)
                {
                    _states.Remove(name);
                    _reported.Add(name);

                    bool sentByApp = _appCopied.Contains(name);
                    var args = new FileTransferredEventArgs(name, fullPath, size);

                    if (sentByApp)
                    {
                        FileSent?.Invoke(this, args);
                    }
                    else
                    {
                        FileReceived?.Invoke(this, args);
                    }

                    _logger.LogDebug(
                        "Transfer reported: {FileName} ({Size} bytes) as {Direction}.",
                        name, size, sentByApp ? "sent" : "received");
                }
            }
            else
            {
                state.Size = size;
                state.StableSamples = 0;
            }
        }

        // A file that vanished is no longer tracked; if it reappears later it
        // is reported again.
        foreach (string name in _states.Keys.Where(name => !seen.Contains(name)).ToList())
        {
            _states.Remove(name);
        }

        foreach (string name in _reported.Where(name => !seen.Contains(name)).ToList())
        {
            _reported.Remove(name);
        }
    }

    private static Dictionary<string, long> ReadExpectedSizes(string folderPath)
    {
        var expectedSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (string metadataPath in Directory.EnumerateFiles(folderPath, $"*{MetadataSuffix}"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("partialName", out JsonElement partialName)
                    || !root.TryGetProperty("totalBytes", out JsonElement totalBytes)
                    || !totalBytes.TryGetInt64(out long expectedSize)
                    || expectedSize <= 0)
                {
                    continue;
                }

                expectedSizes[partialName.GetString() ?? string.Empty] = expectedSize;
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
            {
                // The sender may still be writing the manifest.
            }
        }

        return expectedSizes;
    }

    private sealed class FileState(long size)
    {
        public long Size { get; set; } = size;

        public int StableSamples { get; set; }
    }
}
