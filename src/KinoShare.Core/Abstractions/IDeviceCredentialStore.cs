namespace KinoShare.Core.Abstractions;

/// <summary>
/// Persists the connection password shared by every session so phones only
/// need to enter it once. Implementations must encrypt the value at rest.
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>
    /// Gets the stored password, or null when none has been saved yet.
    /// </summary>
    Task<string?> GetPasswordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the stored password, or generates and persists one using the
    /// factory when nothing is stored yet.
    /// </summary>
    Task<string> GetOrCreatePasswordAsync(Func<string> passwordFactory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored password with a user-chosen value.
    /// </summary>
    Task SetPasswordAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the stored password, if any.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
