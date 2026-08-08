namespace KinoShare.Tests;

using KinoShare.Core.Models;
using KinoShare.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class TransferHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "KinoShareTests",
        "History",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private TransferHistoryService CreateService() => new(
        NullLogger<TransferHistoryService>.Instance,
        _tempDirectory);

    [Fact]
    public async Task Load_NoFile_ReturnsEmpty()
    {
        TransferHistoryService service = CreateService();

        IReadOnlyList<TransferRecord> records = await service.LoadAsync();

        Assert.Empty(records);
    }

    [Fact]
    public async Task Add_ThenLoad_ReturnsNewestFirst()
    {
        TransferHistoryService service = CreateService();
        var older = new TransferRecord("Received", "a.pdf", 100, DateTime.Now.AddMinutes(-10));
        var newer = new TransferRecord("Sent", "b.png", 200, DateTime.Now);

        await service.AddAsync(newer);
        await service.AddAsync(older);

        IReadOnlyList<TransferRecord> records = await service.LoadAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal("a.pdf", records[0].FileName);
        Assert.Equal("b.png", records[1].FileName);
    }

    [Fact]
    public async Task Add_Many_KeepsCap()
    {
        TransferHistoryService service = CreateService();

        for (int i = 0; i < 210; i++)
        {
            await service.AddAsync(new TransferRecord("Received", $"file{i}.bin", i, DateTime.Now));
        }

        IReadOnlyList<TransferRecord> records = await service.LoadAsync();
        Assert.Equal(200, records.Count);
        Assert.Equal("file209.bin", records[0].FileName);
    }

    [Fact]
    public async Task Add_ThenLoad_CorruptFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "history.json"), "not json");

        TransferHistoryService service = CreateService();

        IReadOnlyList<TransferRecord> records = await service.LoadAsync();
        Assert.Empty(records);
    }
}
