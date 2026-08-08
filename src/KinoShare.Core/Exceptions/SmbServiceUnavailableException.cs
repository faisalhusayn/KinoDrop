namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when the Windows SMB server service is not available.
/// </summary>
public sealed class SmbServiceUnavailableException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SmbServiceUnavailableException"/> class.
    /// </summary>
    public SmbServiceUnavailableException(string message)
        : base(message)
    {
    }
}
