namespace KinoShare.Tests.Fakes;

using KinoShare.Core.Abstractions;

/// <summary>In-memory credential store for tests.</summary>
public sealed class FakeCredentialStore : IDeviceCredentialStore
{
    public string? StoredPassword { get; private set; }

    public int FactoryCalls { get; private set; }

    public Task<string?> GetPasswordAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(StoredPassword);

    public Task<string> GetOrCreatePasswordAsync(Func<string> passwordFactory, CancellationToken cancellationToken = default)
    {
        if (StoredPassword is null)
        {
            FactoryCalls++;
            StoredPassword = passwordFactory();
        }

        return Task.FromResult(StoredPassword);
    }

    public Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        StoredPassword = password;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        StoredPassword = null;
        return Task.CompletedTask;
    }
}
