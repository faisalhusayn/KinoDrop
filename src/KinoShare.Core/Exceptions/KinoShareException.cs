namespace KinoShare.Core.Exceptions;

/// <summary>
/// Base type for all KinoShare-specific exceptions.
/// </summary>
public abstract class KinoShareException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KinoShareException"/> class.
    /// </summary>
    protected KinoShareException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KinoShareException"/> class
    /// with an inner exception.
    /// </summary>
    protected KinoShareException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
