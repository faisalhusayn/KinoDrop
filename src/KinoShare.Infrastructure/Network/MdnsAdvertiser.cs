namespace KinoShare.Infrastructure.Network;

using System.Net;
using System.Net.Sockets;
using System.Text;
using KinoShare.Core.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>Advertises KinoDrop as a Bonjour service while a share is active.</summary>
public sealed class MdnsAdvertiser : IDeviceDiscoveryAdvertiser
{
    private const int Port = 5353;
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private readonly INetworkService _network;
    private readonly ILogger<MdnsAdvertiser> _logger;
    private readonly object _sync = new();
    private List<UdpClient> _clients = [];
    private CancellationTokenSource? _cancellation;
    private string? _serviceName;
    private string? _hostName;
    private string? _shareName;

    public MdnsAdvertiser(INetworkService network, ILogger<MdnsAdvertiser> logger)
    {
        _network = network;
        _logger = logger;
    }

    public void Start(string shareName)
    {
        Stop();
        IReadOnlyList<string> addresses = _network.GetPrivateIpAddressesV4();
        if (addresses.Count == 0) return;

        lock (_sync)
        {
            _shareName = shareName;
            _hostName = $"{Environment.MachineName}.local";
            _serviceName = $"{Environment.MachineName}._kinodrop._tcp.local";
            _clients = addresses.Select(address => CreateClient(IPAddress.Parse(address))).ToList();
            _cancellation = new CancellationTokenSource();
            _ = AdvertiseLoopAsync(_cancellation.Token);
        }

        Announce();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            foreach (UdpClient client in _clients) client.Dispose();
            _clients = [];
        }
    }

    public void Dispose() => Stop();

    private async Task AdvertiseLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Announce();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Bonjour listener stopped unexpectedly.");
                return;
            }
        }
    }

    private void Announce()
    {
        IReadOnlyList<string> addresses = _network.GetPrivateIpAddressesV4();
        if (addresses.Count == 0 || _serviceName is null || _hostName is null || _shareName is null) return;
        try
        {
            for (int index = 0; index < Math.Min(addresses.Count, _clients.Count); index++)
            {
                byte[] packet = BuildPacket(IPAddress.Parse(addresses[index]));
                _clients[index].Send(packet, packet.Length, new IPEndPoint(MulticastAddress, Port));
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "Could not advertise the KinoDrop Bonjour service.");
        }
    }

    private static UdpClient CreateClient(IPAddress address)
    {
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        return client;
    }

    private byte[] BuildPacket(IPAddress address)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[2]);
        Write16(writer, 0x8400); Write16(writer, 0); Write16(writer, 4);
        WriteRecord(writer, "_kinodrop._tcp.local", 12, BuildName(_serviceName!), 120);
        WriteRecord(writer, _serviceName!, 33, BuildSrv(), 120);
        byte[] txt = Encoding.ASCII.GetBytes($"share={_shareName}");
        WriteRecord(writer, _serviceName!, 16, [ (byte)txt.Length, ..txt ], 120);
        WriteRecord(writer, _hostName!, 1, address.GetAddressBytes(), 120);
        return stream.ToArray();
    }

    private byte[] BuildSrv()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        Write16(writer, 0); Write16(writer, 0); Write16(writer, 445);
        writer.Write(BuildName(_hostName!));
        return stream.ToArray();
    }

    private static byte[] BuildName(string name)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        foreach (string label in name.TrimEnd('.').Split('.'))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }
        writer.Write((byte)0);
        return stream.ToArray();
    }

    private static void WriteRecord(BinaryWriter writer, string name, ushort type, byte[] data, uint ttl)
    {
        writer.Write(BuildName(name)); Write16(writer, type); Write16(writer, 1); Write32(writer, ttl); Write16(writer, (ushort)data.Length); writer.Write(data);
    }

    private static void Write16(BinaryWriter writer, ushort value) => writer.Write(BitConverter.IsLittleEndian ? (ushort)((value << 8) | (value >> 8)) : value);
    private static void Write32(BinaryWriter writer, uint value) => writer.Write(BitConverter.IsLittleEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value) : value);
}
