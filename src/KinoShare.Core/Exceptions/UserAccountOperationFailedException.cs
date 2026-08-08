namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when creating or deleting a temporary Windows account fails.
/// </summary>
public sealed class UserAccountOperationFailedException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserAccountOperationFailedException"/> class.
    /// </summary>
    public UserAccountOperationFailedException(string operation, string detail)
        : base($"Windows failed to {operation} the temporary user: {detail}")
    {
        Operation = operation;
    }

    /// <summary>
    /// Gets the operation that failed (for example "create" or "delete").
    /// </summary>
    public string Operation { get; }
}
