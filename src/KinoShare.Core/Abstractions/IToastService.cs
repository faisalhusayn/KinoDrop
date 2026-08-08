namespace KinoShare.Core.Abstractions;

/// <summary>
/// Shows desktop notifications for completed transfers. Implementations must
/// never throw; failures are swallowed so a notification problem can never
/// break a transfer.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Shows a notification that a transfer completed.
    /// </summary>
    /// <param name="direction">"Received" or "Sent".</param>
    /// <param name="fileName">The file name without its directory.</param>
    /// <param name="sizeText">A display-friendly size, e.g. "12.4 MB".</param>
    void ShowTransferCompleted(string direction, string fileName, string sizeText);
}
