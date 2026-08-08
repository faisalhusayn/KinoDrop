namespace KinoShare.Core.Abstractions;

using KinoShare.Core.Models;

/// <summary>
/// Abstraction over Windows' native SMB share management.
/// Implementations must rely on the Windows SMB server and must never
/// implement the SMB protocol themselves.
/// </summary>
public interface ISmbShareService
{
    /// <summary>
    /// Creates an SMB share for the folder described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The folder and share name to use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created share's information.</returns>
    /// <exception cref="KinoShare.Core.Exceptions.ShareAlreadyExistsException">
    /// Thrown when a share with the requested name already exists.
    /// </exception>
    /// <exception cref="KinoShare.Core.Exceptions.ShareOperationFailedException">
    /// Thrown when the Windows SMB API reports a failure.
    /// </exception>
    Task<ShareInfo> CreateShareAsync(ShareRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the SMB share with the specified name.
    /// </summary>
    /// <param name="shareName">The name of the share to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="KinoShare.Core.Exceptions.ShareOperationFailedException">
    /// Thrown when the Windows SMB API reports a failure.
    /// </exception>
    Task RemoveShareAsync(string shareName, CancellationToken cancellationToken = default);
}
