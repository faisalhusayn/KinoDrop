namespace KinoShare.Infrastructure.Smb;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Core.Validation;
using KinoShare.Infrastructure.PowerShell;
using Microsoft.Extensions.Logging;

/// <summary>
/// Drives Windows' native SMB server through the built-in PowerShell
/// <c>SmbShare</c> cmdlets. No SMB protocol code lives here: Windows does
/// all the work, this class only automates it.
/// </summary>
public sealed class PowerShellSmbShareService : ISmbShareService
{
    private const string CreateVerb = "create";
    private const string RemoveVerb = "remove";

    private readonly ILogger<PowerShellSmbShareService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellSmbShareService"/> class.
    /// </summary>
    public PowerShellSmbShareService(ILogger<PowerShellSmbShareService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ShareInfo> CreateShareAsync(ShareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ShareNameValidator.Validate(request.ShareName);

        string grantee = request.GrantAccessTo is null
            ? "Everyone"
            : $@"{Environment.MachineName}\{request.GrantAccessTo}";

        var arguments = new List<string>
        {
            request.ShareName + "=" + request.FolderPath,
        };
        if (request.GrantAccessTo is not null)
        {
            arguments.Add($"/grant:{grantee},FULL");
        }

        NativeCommandInvoker.CommandResult result = await NativeCommandInvoker.InvokeAsync(
            "net.exe",
            ["share", .. arguments],
            cancellationToken);

        if (!result.Succeeded)
        {
            throw MapFailure(CreateVerb, result, request.ShareName);
        }

        _logger.LogDebug("New-SmbShare completed for {ShareName} granted to {Grantee}.", request.ShareName, grantee);

        string uncPath = $@"\\{Environment.MachineName}\{request.ShareName}";
        return new ShareInfo(request.ShareName, request.FolderPath, uncPath);
    }

    /// <inheritdoc />
    public async Task RemoveShareAsync(string shareName, CancellationToken cancellationToken = default)
    {
        ShareNameValidator.Validate(shareName);

        NativeCommandInvoker.CommandResult result = await NativeCommandInvoker.InvokeAsync(
            "net.exe",
            ["share", shareName, "/delete", "/y"],
            cancellationToken);

        if (!result.Succeeded)
        {
            throw MapFailure(RemoveVerb, result, shareName);
        }

        _logger.LogDebug("Remove-SmbShare completed for {ShareName}.", shareName);
    }

    private KinoShareException MapFailure(
        string operation,
        NativeCommandInvoker.CommandResult result,
        string shareName)
    {
        string detail = result.Detail;

        _logger.LogError("PowerShell failed during {Operation} (exit {ExitCode}): {Detail}",
            operation, result.ExitCode, detail);

        if (detail.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("already been shared", StringComparison.OrdinalIgnoreCase))
        {
            return new ShareAlreadyExistsException(shareName);
        }

        if (detail.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return new SmbServiceUnavailableException(
                "Administrator rights are required to manage SMB shares.");
        }

        if (detail.Contains("not running", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("cannot be found", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("service", StringComparison.OrdinalIgnoreCase))
        {
            return new SmbServiceUnavailableException(
                "The Windows SMB server service is unavailable. Ensure the 'Server' service is running.");
        }

        return new ShareOperationFailedException(operation, detail);
    }
}
