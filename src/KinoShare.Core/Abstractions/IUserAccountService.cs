namespace KinoShare.Core.Abstractions;

using KinoShare.Core.Models;

/// <summary>
/// Manages the lifecycle of temporary local Windows accounts used for sharing.
/// </summary>
public interface IUserAccountService
{
    /// <summary>
    /// Creates (or resets the password of an existing) local account for
    /// <paramref name="user"/>. The account is a standard user, never an admin.
    /// </summary>
    /// <param name="user">The username and password to provision.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task CreateTemporaryUserAsync(TemporaryUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the local account with the given username.
    /// </summary>
    /// <param name="username">The account to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteUserAsync(string username, CancellationToken cancellationToken = default);
}
