namespace KinoShare.Core.Abstractions;

/// <summary>
/// Watches the workspace's single transfer folder and raises events as files
/// arrive. Files that the app itself placed there (via "send") are reported
/// as sent; everything else that appears is reported as received.
/// Transport-agnostic: the events fire regardless of how the file got there
/// (SMB, drag and drop, ...).
/// </summary>
public interface ITransferMonitorService : IDisposable
{
    /// <summary>
    /// Raised when a file has fully arrived in the transfer folder and was
    /// not placed there by the app itself (a transfer from the phone).
    /// </summary>
    event EventHandler<KinoShare.Core.Models.FileTransferredEventArgs>? FileReceived;

    /// <summary>
    /// Raised when a file that the app placed in the transfer folder (via
    /// "send") has fully arrived (a transfer staged for the phone).
    /// </summary>
    event EventHandler<KinoShare.Core.Models.FileTransferredEventArgs>? FileSent;

    /// <summary>
    /// Raised repeatedly while a file is still arriving, reporting the bytes
    /// observed so far. The final event for a file is <see cref="FileReceived"/>
    /// or <see cref="FileSent"/>.
    /// </summary>
    event EventHandler<KinoShare.Core.Models.FileProgressEventArgs>? FileProgress;

    /// <summary>
    /// Starts watching the transfer folder. Files already present are seeded
    /// and never reported as transfers. Safe to call once per session.
    /// </summary>
    /// <param name="transferFolderPath">The folder to watch.</param>
    void Start(string transferFolderPath);

    /// <summary>
    /// Marks a file as placed by the app so the monitor reports it as sent.
    /// Must be called after the file has been copied into the transfer folder.
    /// </summary>
    /// <param name="fileName">The file name (without its directory).</param>
    void RegisterAppCopiedFile(string fileName);

    /// <summary>
    /// Stops watching and releases all resources. Safe to call multiple times.
    /// </summary>
    void Stop();
}
