namespace KinoShare.Infrastructure.Network;

using System.Net.NetworkInformation;
using System.Net.Sockets;
using KinoShare.Core.Abstractions;

/// <summary>
/// Inspects local network interfaces to find the primary private IPv4
/// address. Display-only: SMB transport itself is entirely handled by Windows.
/// </summary>
public sealed class NetworkService : INetworkService
{
    /// <inheritdoc />
    public string? GetPrimaryPrivateIpAddressV4()
        => GetPrivateIpAddressesV4().FirstOrDefault();

    /// <inheritdoc />
    public IReadOnlyList<string> GetPrivateIpAddressesV4()
    {
        var addresses = new List<string>();
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                byte[] octets = address.Address.GetAddressBytes();
                bool isPrivate =
                    octets[0] == 10
                    || (octets[0] == 172 && octets[1] is >= 16 and <= 31)
                    || (octets[0] == 192 && octets[1] == 168);

                if (isPrivate)
                {
                    addresses.Add(address.Address.ToString());
                }
            }
        }

        return addresses;
    }
}
