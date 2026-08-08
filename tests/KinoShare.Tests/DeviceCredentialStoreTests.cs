namespace KinoShare.Tests;

using KinoShare.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DeviceCredentialStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "KinoShareTests",
        "Credentials",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private DeviceCredentialStore CreateStore() => new(
        NullLogger<DeviceCredentialStore>.Instance,
        _tempDirectory);

    [Fact]
    public async Task Get_NoFile_ReturnsNull()
    {
        DeviceCredentialStore store = CreateStore();

        string? password = await store.GetPasswordAsync();

        Assert.Null(password);
    }

    [Fact]
    public async Task Set_ThenGet_ReturnsSamePassword()
    {
        DeviceCredentialStore store = CreateStore();

        await store.SetPasswordAsync("my-pass-123");

        Assert.Equal("my-pass-123", await store.GetPasswordAsync());
    }

    [Fact]
    public async Task Set_OverwritesPreviousValue()
    {
        DeviceCredentialStore store = CreateStore();

        await store.SetPasswordAsync("first-pass");
        await store.SetPasswordAsync("second-pass");

        Assert.Equal("second-pass", await store.GetPasswordAsync());
    }

    [Fact]
    public async Task GetOrCreate_NoStoredValue_GeneratesAndPersists()
    {
        DeviceCredentialStore store = CreateStore();
        var factoryCalls = 0;

        string password = await store.GetOrCreatePasswordAsync(() => $"generated-{++factoryCalls}");

        Assert.Equal("generated-1", password);
        Assert.Equal("generated-1", await store.GetPasswordAsync());

        string second = await store.GetOrCreatePasswordAsync(() => $"generated-{++factoryCalls}");

        Assert.Equal("generated-1", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task Clear_RemovesStoredPassword()
    {
        DeviceCredentialStore store = CreateStore();
        await store.SetPasswordAsync("my-pass-123");

        await store.ClearAsync();

        Assert.Null(await store.GetPasswordAsync());
    }

    [Fact]
    public async Task Get_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_tempDirectory, "credential.bin"), [1, 2, 3, 4, 5]);

        DeviceCredentialStore store = CreateStore();

        Assert.Null(await store.GetPasswordAsync());
    }

    [Fact]
    public async Task Set_EmptyPassword_Throws()
    {
        DeviceCredentialStore store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetPasswordAsync(string.Empty));
    }
}
