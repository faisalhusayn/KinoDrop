namespace KinoShare.Core.Abstractions;

using KinoShare.Core.Models;

/// <summary>
/// Persists the history of completed transfers so the live feed survives
/// app restarts.
/// </summary>
public interface ITransferHistoryService
{
    /// <summary>
    /// Loads the persisted transfers, newest first, or an empty list when
    /// none exist yet.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The persisted transfers, newest first.</returns>
    Task<IReadOnlyList<TransferRecord>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a completed transfer to the history. The newest entry is kept
    /// first, and older entries beyond the cap are dropped.
    /// </summary>
    /// <param name="record">The transfer to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AddAsync(TransferRecord record, CancellationToken cancellationToken = default);
}
