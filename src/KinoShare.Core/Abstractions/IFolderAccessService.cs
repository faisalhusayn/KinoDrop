namespace KinoShare.Core.Abstractions;

/// <summary>
/// Adjusts Windows folder (NTFS) permissions for the folder being shared.
/// Milestone-2 scope: granting read/write to a specific account only.
/// </summary>
public interface IFolderAccessService
{
    /// <summary>
    /// Grants <paramref name="accountName"/> read and write access (Modify)
    /// to <paramref name="folderPath"/> and everything inside it.
    /// </summary>
    /// <param name="folderPath">The folder to grant access to.</param>
    /// <param name="accountName">The account to grant access to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task GrantReadWriteAsync(string folderPath, string accountName, CancellationToken cancellationToken = default);
}
