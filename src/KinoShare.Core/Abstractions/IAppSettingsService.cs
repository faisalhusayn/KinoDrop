namespace KinoShare.Core.Abstractions;

/// <summary>
/// Persists application settings in the workspace's Settings folder.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Gets the user-chosen transfer folder location, or null when the
    /// default location is in use.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The chosen location, or null.</returns>
    Task<string?> GetTransferFolderLocationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears with null) the user-chosen transfer folder location.
    /// </summary>
    /// <param name="location">The chosen location, or null for the default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetTransferFolderLocationAsync(string? location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the user-chosen UI theme ("Light", "Dark"), or null to follow
    /// the system setting.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<string?> GetThemeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or clears with null) the user-chosen UI theme.
    /// </summary>
    /// <param name="theme">"Light", "Dark", or null to follow the system.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetThemeAsync(string? theme, CancellationToken cancellationToken = default);
}
