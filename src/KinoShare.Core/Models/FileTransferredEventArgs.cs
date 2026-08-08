namespace KinoShare.Core.Models;

/// <summary>
/// Describes a file that has finished arriving in one of the watched
/// transfer folders.
/// </summary>
/// <param name="FileName">The name of the file, without its directory.</param>
/// <param name="FullPath">The full path of the file.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="Timestamp">When the file was detected as complete.</param>
public sealed record FileTransferredEventArgs(
    string FileName,
    string FullPath,
    long Size,
    DateTime Timestamp)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransferredEventArgs"/> class.
    /// </summary>
    public FileTransferredEventArgs(string fileName, string fullPath, long size)
        : this(fileName, fullPath, size, DateTime.Now)
    {
    }
}
