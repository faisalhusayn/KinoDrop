namespace KinoShare.Infrastructure.Users;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Infrastructure.PowerShell;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adjusts NTFS permissions on shared folders via <c>icacls</c>: the target
/// folder gets Modify for the sharing account.
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

        // Qualify the local account so icacls does not attempt a slow domain
        // lookup for the unqualified username on each ACL operation.
        string localAccountName = $@"{Environment.MachineName}\{accountName}";

        // Windows grants local users the traverse privilege by default, so
        // changing every parent directory is unnecessary and very slow on
        // profile folders. Grant only the permissions needed on the share.
        string command =
            $"icacls '{PowerShellInvoker.Escape(folderPath)}' /grant " +
            $"'{PowerShellInvoker.Escape(localAccountName)}:(OI)(CI)M'; " +
            "if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);

        if (!result.Succeeded)
        {
            throw new ShareOperationFailedException("grant access to", result.Detail);
        }

        _logger.LogDebug("Granted {AccountName} Modify on {FolderPath}.", accountName, folderPath);
    }
}
