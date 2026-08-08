namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when the requested share name is not valid for Windows SMB.
/// </summary>
public sealed class InvalidShareNameException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidShareNameException"/> class.
    /// </summary>
    public InvalidShareNameException(string shareName, string reason)
        : base($"The share name '{shareName}' is not valid: {reason}")
    {
        ShareName = shareName;
    }

    /// <summary>
    /// Gets the share name that failed validation.
    /// </summary>
    public string ShareName { get; }
}
