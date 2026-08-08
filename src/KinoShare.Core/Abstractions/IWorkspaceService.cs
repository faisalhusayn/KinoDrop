namespace KinoShare.Core.Abstractions;

using KinoShare.Core.Models;

/// <summary>
/// Creates and describes the KinoShare workspace: a single transfer folder
/// (created by the app at a default or user-chosen location) plus app-internal
/// folders (temp, logs, settings) that are never shared.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Ensures the workspace and all its folders exist, creating them on
    /// first run. Safe to call repeatedly.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The paths of the workspace.</returns>
    /// <exception cref="KinoShare.Core.Exceptions.WorkspaceException">
    /// Thrown when the workspace cannot be created or the transfer folder
    /// would sit on a filesystem that cannot hold the required permissions.
    /// </exception>
    Task<WorkspaceInfo> EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
