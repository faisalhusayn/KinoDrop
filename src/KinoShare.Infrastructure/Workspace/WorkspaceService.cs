namespace KinoShare.Infrastructure.Workspace;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Creates the KinoShare workspace: the app home under %LOCALAPPDATA% plus a
/// single transfer folder that is the only directory ever exposed over SMB.
/// The transfer folder defaults to <c>%LOCALAPPDATA%\KinoShare\Share</c> and
/// can be moved to a user-chosen location; a dedicated folder is always
/// created by the app (never a pre-existing folder).
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private const string NtfsDriveFormat = "NTFS";

    private readonly ILogger<WorkspaceService> _logger;
    private readonly string _rootPath;
    private readonly string _transferFolderPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceService"/> class
    /// with the default app home and default transfer folder.
    /// </summary>
    public WorkspaceService(ILogger<WorkspaceService> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceService"/> class
    /// rooted at %LOCALAPPDATA%\KinoShare with the given transfer folder
    /// location. When <paramref name="transferFolderLocation"/> is null the
    /// default transfer folder is used.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="transferFolderLocation">
    /// A user-chosen location; a <c>KinoShare</c> folder is created inside it.
    /// When null, the default transfer folder is used.
    /// </param>
    public WorkspaceService(ILogger<WorkspaceService> logger, string? transferFolderLocation)
        : this(logger,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceInfo.RootFolderName),
            ResolveTransferFolder(transferFolderLocation))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceService"/> class
    /// with explicit paths. Used by tests to keep the real user workspace
    /// untouched.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="rootPath">The app home directory.</param>
    /// <param name="transferFolderPath">The resolved transfer folder path.</param>
    public WorkspaceService(ILogger<WorkspaceService> logger, string rootPath, string transferFolderPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _transferFolderPath = transferFolderPath ?? throw new ArgumentNullException(nameof(transferFolderPath));
    }

    /// <summary>
    /// Resolves a user-chosen location to the actual transfer folder path.
    /// </summary>
    /// <param name="location">
    /// A directory chosen by the user, or null for the default. If the path's
    /// last segment is already "KinoShare" it is used as-is; otherwise a
    /// dedicated "KinoShare" folder is appended inside it.
    /// </param>
    /// <returns>The transfer folder path.</returns>
    public static string ResolveTransferFolder(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceInfo.RootFolderName,
                WorkspaceInfo.DefaultTransferFolderName);
        }

        string trimmed = Path.GetFullPath(location.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string lastSegment = Path.GetFileName(trimmed);

        return string.Equals(lastSegment, WorkspaceInfo.RootFolderName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, WorkspaceInfo.RootFolderName);
    }

    /// <inheritdoc />
    public Task<WorkspaceInfo> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        WorkspaceInfo workspace = Describe();

        EnsureNtfsDrive(workspace.TransferFolderPath);

        List<string> directories =
        [
            workspace.RootPath,
            workspace.TransferFolderPath,
            workspace.TempPath,
            workspace.LogsPath,
            workspace.SettingsPath,
        ];

        try
        {
            foreach (string directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(directory);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create the KinoShare workspace under {RootPath}.", _rootPath);
            throw new WorkspaceException($"The KinoShare workspace could not be created under '{_rootPath}'.", exception);
        }

        _logger.LogDebug(
            "Workspace ready: root {Root}, transfer folder {Transfer}.",
            workspace.RootPath, workspace.TransferFolderPath);

        return Task.FromResult(workspace);
    }

    private WorkspaceInfo Describe() => new(
        RootPath: _rootPath,
        TransferFolderPath: _transferFolderPath,
        TempPath: Path.Combine(_rootPath, WorkspaceInfo.TempFolderName),
        LogsPath: Path.Combine(_rootPath, WorkspaceInfo.LogsFolderName),
        SettingsPath: Path.Combine(_rootPath, WorkspaceInfo.SettingsFolderName));

    private void EnsureNtfsDrive(string transferFolderPath)
    {
        try
        {
            string? driveRoot = Path.GetPathRoot(transferFolderPath);
            if (driveRoot is null)
            {
                return;
            }

            var drive = new DriveInfo(driveRoot);
            if (drive.IsReady && !string.Equals(drive.DriveFormat, NtfsDriveFormat, StringComparison.OrdinalIgnoreCase))
            {
                throw new WorkspaceException(
                    $"The transfer folder location '{driveRoot}' is on a {drive.DriveFormat} drive. " +
                    "Folder permissions (required for SMB sharing) are only supported on NTFS drives.");
            }
        }
        catch (WorkspaceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The drive may be inaccessible at this point; the folder creation
            // step below will surface the real problem with a clearer message.
            _logger.LogDebug(exception, "Could not inspect the drive for {TransferFolderPath}.", transferFolderPath);
        }
    }
}
