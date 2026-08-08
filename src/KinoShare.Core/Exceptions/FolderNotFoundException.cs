namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when the folder requested for sharing does not exist.
/// </summary>
public sealed class FolderNotFoundException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FolderNotFoundException"/> class.
    /// </summary>
    public FolderNotFoundException(string folderPath)
        : base($"The folder '{folderPath}' does not exist.")
    {
        FolderPath = folderPath;
    }

    /// <summary>
    /// Gets the folder path that could not be found.
    /// </summary>
    public string FolderPath { get; }
}
