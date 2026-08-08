namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when a share with the requested name already exists on this machine.
/// </summary>
public sealed class ShareAlreadyExistsException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareAlreadyExistsException"/> class.
    /// </summary>
    public ShareAlreadyExistsException(string shareName)
        : base($"A share named '{shareName}' already exists on this computer.")
    {
        ShareName = shareName;
    }

    /// <summary>
    /// Gets the name of the share that already exists.
    /// </summary>
    public string ShareName { get; }
}
