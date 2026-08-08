namespace KinoShare.Infrastructure.Users;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Infrastructure.PowerShell;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provisions and deletes temporary local Windows accounts via the native
/// <c>net.exe</c> command. Using a native executable avoids PowerShell module
/// autoload issues (the Security module is unreliable in some environments).
/// A leftover account with the requested name is reused (password reset), so
/// interrupted sessions self-heal on the next run.
/// </summary>
public sealed class NetUserAccountService : IUserAccountService
{
    private readonly ILogger<NetUserAccountService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetUserAccountService"/> class.
    /// </summary>
    public NetUserAccountService(ILogger<NetUserAccountService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task CreateTemporaryUserAsync(TemporaryUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        string name = PowerShellInvoker.Escape(user.Username);
        string password = PowerShellInvoker.Escape(user.Password);

        // Try to create; if the account already exists (leftover from an
        // interrupted session), fall back to resetting its password instead.
        string command =
            $"net user '{name}' '{password}' /add /passwordchg:no /expires:never | Out-Null; " +
            $"if ($LASTEXITCODE -ne 0) {{ " +
            $"net user '{name}' '{password}' /passwordchg:no /expires:never | Out-Null " +
            $"}}; " +
            $"exit $LASTEXITCODE";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);

        if (!result.Succeeded)
        {
            throw new UserAccountOperationFailedException("create", result.Detail);
        }

        _logger.LogDebug("Temporary user {Username} provisioned (created or password reset).", user.Username);
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // Exit code 2 means the account does not exist, which is fine here.
        string command =
            $"net user '{PowerShellInvoker.Escape(username)}' /delete | Out-Null; " +
            $"if ($LASTEXITCODE -eq 2) {{ exit 0 }} else {{ exit $LASTEXITCODE }}";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);

        if (!result.Succeeded)
        {
            throw new UserAccountOperationFailedException("delete", result.Detail);
        }

        _logger.LogDebug("Temporary user {Username} deleted.", username);
    }
}
