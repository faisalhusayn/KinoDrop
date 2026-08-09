namespace KinoShare.Core.Models;

/// <summary>
/// Reports the current size of a file that is still arriving in the transfer
/// folder. Raised repeatedly while the file is being copied, until the file
/// completes and a <see cref="FileTransferredEventArgs"/> is raised instead.
/// </summary>
/// <param name="FileName">The name of the file, without its directory.</param>
/// <param name="FullPath">The full path of the file.</param>
/// <param name="BytesCopied">The number of bytes observed so far.</param>
/// <param name="IsAppCopy">True when the file was placed by the app itself (a send).</param>
/// <param name="Timestamp">When this progress sample was taken.</param>
/// <param name="TotalBytes">The expected size when the sender published it.</param>
public sealed record FileProgressEventArgs(
    string FileName,
    string FullPath,
    long BytesCopied,
    bool IsAppCopy,
    DateTime Timestamp,
    long? TotalBytes = null)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileProgressEventArgs"/> class.
    /// </summary>
    public FileProgressEventArgs(string fileName, string fullPath, long bytesCopied, bool isAppCopy)
        : this(fileName, fullPath, bytesCopied, isAppCopy, DateTime.Now)
    {
    }
}
