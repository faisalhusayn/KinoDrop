namespace KinoShare.Core.Abstractions;

/// <summary>Publishes the active share on the local network without credentials.</summary>
public interface IDeviceDiscoveryAdvertiser : IDisposable
{
    void Start(string shareName);

    void Stop();
}
