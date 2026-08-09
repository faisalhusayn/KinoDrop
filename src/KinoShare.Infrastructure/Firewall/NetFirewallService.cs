namespace KinoShare.Infrastructure.Firewall;

using KinoShare.Core.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
        // Use netsh directly instead of loading the NetSecurity PowerShell
        // module. The latter can take many seconds on some Windows systems.
        CommandResult result = await InvokeNetshAsync(
            ["advfirewall", "firewall", "add", "rule", $"name={RuleName}", "dir=in", "action=allow", "protocol=TCP", "localport=445", "profile=any"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            // A leftover rule from an interrupted session is already usable.
            CommandResult existing = await InvokeNetshAsync(
                ["advfirewall", "firewall", "show", "rule", $"name={RuleName}"],
                cancellationToken);
            if (existing.ExitCode != 0)
            {
                _logger.LogWarning("Could not create the firewall rule: {Detail}", result.Detail);
                return false;
            }
        }

        _logger.LogDebug("Firewall rule {RuleName} is in place (inbound TCP 445, all profiles).", RuleName);
        return true;
    }

    /// <inheritdoc />
    public async Task RestoreSmbInboundAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await InvokeNetshAsync(
            ["advfirewall", "firewall", "delete", "rule", $"name={RuleName}"],
            cancellationToken);

        if (result.ExitCode != 0 && !result.Detail.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Could not remove the firewall rule: {result.Detail}");
        }

        _logger.LogDebug("Firewall rule {RuleName} removed.", RuleName);
    }

    /// <inheritdoc />
    public async Task<bool> IsSmbInboundAllowedAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await InvokeNetshAsync(
            ["advfirewall", "firewall", "show", "rule", $"name={RuleName}"],
            cancellationToken);
        return result.ExitCode == 0;
    }

    private static async Task<CommandResult> InvokeNetshAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new CommandResult(-1, string.Empty, "Unable to start netsh.exe.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new CommandResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited; nothing to kill.
            }

            throw;
        }
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string Detail => !string.IsNullOrWhiteSpace(StandardError)
            ? StandardError.Trim()
            : StandardOutput.Trim();
    }
}
