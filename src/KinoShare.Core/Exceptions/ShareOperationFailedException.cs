namespace KinoShare.Core.Exceptions;

/// <summary>
/// Thrown when Windows reports a failure while creating or removing an SMB share.
/// </summary>
public sealed class ShareOperationFailedException : KinoShareException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareOperationFailedException"/> class.
    /// </summary>
    public ShareOperationFailedException(string operation, string detail)
        : base($"Windows failed to {operation} the SMB share: {detail}")
    {
        Operation = operation;
    }

    /// <summary>
    /// Gets the operation that failed (for example "create" or "remove").
    /// </summary>
    public string Operation { get; }
}
