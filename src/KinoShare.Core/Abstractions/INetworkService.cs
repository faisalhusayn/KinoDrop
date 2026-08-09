namespace KinoShare.Core.Abstractions;

/// <summary>
/// Provides information about the machine's network connectivity, used only
/// to present connection hints to the user. Never used for data transfer.
/// </summary>
public interface INetworkService
{
    /// <summary>Returns all active private IPv4 addresses on this machine.</summary>
    IReadOnlyList<string> GetPrivateIpAddressesV4();

    /// <summary>
    /// Returns the primary private IPv4 address clients can use to reach
    /// this machine, or <c>null</c> when no suitable address exists.
    /// </summary>
    string? GetPrimaryPrivateIpAddressV4();
}
