namespace KinoShare.Infrastructure.Users;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Infrastructure.PowerShell;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adjusts NTFS permissions on shared folders via <c>icacls</c>: the target
/// folder gets Modify for the sharing account, and every ancestor directory
/// up to the drive root gets a traverse-only grant so the account can reach
/// the folder through locked-down paths such as user profiles.
/// </summary>
public sealed class IcaclsFolderAccessService : IFolderAccessService
{
    private readonly ILogger<IcaclsFolderAccessService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IcaclsFolderAccessService"/> class.
    /// </summary>
    public IcaclsFolderAccessService(ILogger<IcaclsFolderAccessService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task GrantReadWriteAsync(string folderPath, string accountName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        // A share can only be reached if the account can traverse every
        // ancestor directory up to the drive root. Default user-profile ACLs
        // do not grant this (AppData is locked down), and SMB clients report
        // it as a generic failure. Traverse-only grants are minimal and safe.
        //
        // Spawning PowerShell is the expensive part, so all icacls calls are
        // batched into a single invocation that fails fast on the first error.
        var commands = new List<string>
        {
            $"icacls '{PowerShellInvoker.Escape(folderPath)}' /grant '{PowerShellInvoker.Escape(accountName)}:(OI)(CI)M'",
        };

        string? ancestor = Path.GetDirectoryName(folderPath);
        string? driveRoot = Path.GetPathRoot(folderPath);

        while (!string.IsNullOrEmpty(ancestor)
            && !string.Equals(ancestor, driveRoot, StringComparison.OrdinalIgnoreCase))
        {
            commands.Add($"icacls '{PowerShellInvoker.Escape(ancestor)}' /grant '{PowerShellInvoker.Escape(accountName)}:(X)'");
            ancestor = Path.GetDirectoryName(ancestor);
        }

        string batch = string.Join(
            Environment.NewLine,
            commands.Select(command => $"& {command}; if ($LASTEXITCODE -ne 0) {{ Write-Error \"{command}\"; exit 1 }}"));

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(batch, cancellationToken);

        if (!result.Succeeded)
        {
            throw new ShareOperationFailedException("grant access to", result.Detail);
        }

        _logger.LogDebug("Granted {AccountName} Modify on {FolderPath}.", accountName, folderPath);
    }
}
