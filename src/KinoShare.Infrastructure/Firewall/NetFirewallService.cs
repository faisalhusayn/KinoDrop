namespace KinoShare.Infrastructure.Firewall;

using KinoShare.Core.Abstractions;
using KinoShare.Infrastructure.PowerShell;
using Microsoft.Extensions.Logging;

/// <summary>
/// Creates and removes a narrow Windows Firewall rule that allows inbound SMB
/// (TCP 445) while a sharing session runs, so the iPhone can connect even on
/// Public network profiles. The rule is named <c>KinoShare SMB</c> and is
/// removed again when the session stops; existing File and Printer Sharing
/// rules are never modified.
/// </summary>
public sealed class NetFirewallService : IFirewallService
{
    private const string RuleName = "KinoShare SMB";

    private readonly ILogger<NetFirewallService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetFirewallService"/> class.
    /// </summary>
    public NetFirewallService(ILogger<NetFirewallService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> AllowSmbInboundAsync(CancellationToken cancellationToken = default)
    {
        // Remove any rule left behind by a crashed session, then create a
        // fresh one. Scoped to TCP 445 only, all profiles.
        string command =
            $"Remove-NetFirewallRule -Name '{RuleName}' -ErrorAction SilentlyContinue; " +
            $"New-NetFirewallRule -Name '{RuleName}' -DisplayName '{RuleName}' " +
            $"-Direction Inbound -Protocol TCP -LocalPort 445 -Action Allow -Profile Any; " +
            $"(Get-NetFirewallRule -Name '{RuleName}' -ErrorAction SilentlyContinue) -ne $null";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Could not create the firewall rule: {Detail}", result.Detail);
            return false;
        }

        bool inPlace = result.StandardOutput.Trim() is "True" or "true";
        _logger.LogDebug(
            "Firewall rule {RuleName} created (inbound TCP 445, all profiles); in place: {InPlace}.",
            RuleName, inPlace);
        return inPlace;
    }

    /// <inheritdoc />
    public async Task RestoreSmbInboundAsync(CancellationToken cancellationToken = default)
    {
        string command = $"Remove-NetFirewallRule -Name '{RuleName}' -ErrorAction SilentlyContinue";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not remove the firewall rule: {result.Detail}");
        }

        _logger.LogDebug("Firewall rule {RuleName} removed.", RuleName);
    }

    /// <inheritdoc />
    public async Task<bool> IsSmbInboundAllowedAsync(CancellationToken cancellationToken = default)
    {
        string command = $"(Get-NetFirewallRule -Name '{RuleName}' -ErrorAction SilentlyContinue) -ne $null";

        PowerShellInvoker.PowerShellResult result = await PowerShellInvoker.InvokeAsync(command, cancellationToken);
        return result.Succeeded && result.StandardOutput.Trim() is "True" or "true";
    }
}
