namespace KinoShare.Core.Abstractions;

/// <summary>
/// Manages the Windows firewall rules needed for inbound SMB access to the
/// share, so sessions work even on Public network profiles where Windows
/// blocks file sharing by default.
/// </summary>
public interface IFirewallService
{
    /// <summary>
    /// Allows inbound SMB (TCP 445) for the session. Idempotent; safe to call
    /// again if a rule was left behind by a crashed session.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the rule is in place after the call.</returns>
    Task<bool> AllowSmbInboundAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the inbound SMB rule created by <see cref="AllowSmbInboundAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RestoreSmbInboundAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the inbound SMB rule created by this service is present.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>True when the rule exists.</returns>
    Task<bool> IsSmbInboundAllowedAsync(CancellationToken cancellationToken = default);
}
