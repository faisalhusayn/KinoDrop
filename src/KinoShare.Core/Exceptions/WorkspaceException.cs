namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when the KinoShare workspace cannot be created or described.
/// </summary>
public sealed class WorkspaceException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceException"/> class.
    /// </summary>
    public WorkspaceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceException"/> class
    /// with an inner exception.
    /// </summary>
    public WorkspaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
