namespace KinoShare.Tests;

using KinoShare.Core.Models;
using KinoShare.Infrastructure.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="FileSystemTransferMonitor"/>. Uses a short poll
/// interval so stabilization checks complete quickly.
/// </summary>
public class FileSystemTransferMonitorTests
{
    [Fact]
    public async Task Start_FileAppears_RaisesFileReceived()
    {
        using var fixture = new MonitorFixture();

        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50));

        var received = new TaskCompletionSource<FileTransferredEventArgs>();
        monitor.FileReceived += (_, e) => received.TrySetResult(e);

        monitor.Start(fixture.Folder);

        string filePath = Path.Combine(fixture.Folder, "IMG_1201.MOV");
        await File.WriteAllTextAsync(filePath, "video-data");

        FileTransferredEventArgs args = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("IMG_1201.MOV", args.FileName);
        Assert.Equal(filePath, args.FullPath);
        Assert.Equal(10, args.Size);
    }

    [Fact]
    public async Task Start_AppCopiedFile_RaisesFileSent()
    {
        using var fixture = new MonitorFixture();

        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50));

        var sent = new TaskCompletionSource<FileTransferredEventArgs>();
        monitor.FileSent += (_, e) => sent.TrySetResult(e);

        monitor.Start(fixture.Folder);

        string filePath = Path.Combine(fixture.Folder, "edit.mp4");
        await File.WriteAllTextAsync(filePath, "clip");
        monitor.RegisterAppCopiedFile("edit.mp4");

        FileTransferredEventArgs args = await sent.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("edit.mp4", args.FileName);
        Assert.Equal(4, args.Size);
    }

    [Fact]
    public async Task Start_FileAlreadyPresent_IsSeededAndNeverReported()
    {
        using var fixture = new MonitorFixture();
        string existing = Path.Combine(fixture.Folder, "old.zip");
        await File.WriteAllTextAsync(existing, "old data");

        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50));

        var received = new TaskCompletionSource<FileTransferredEventArgs>();
        monitor.FileReceived += (_, e) => received.TrySetResult(e);

        monitor.Start(fixture.Folder);

        await Assert.ThrowsAsync<TimeoutException>(
            () => received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Start_FileKeptGrowing_DoesNotReportUntilStable()
    {
        using var fixture = new MonitorFixture();

        // Three consecutive identical samples are required to report, so a
        // file that grows every 60ms never stabilizes until writes stop.
        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50),
            stableSamples: 3);

        var received = new TaskCompletionSource<FileTransferredEventArgs>();
        int eventCount = 0;
        monitor.FileReceived += (_, e) =>
        {
            eventCount++;
            received.TrySetResult(e);
        };

        monitor.Start(fixture.Folder);

        string filePath = Path.Combine(fixture.Folder, "growing.bin");
        for (int i = 0; i < 6; i++)
        {
            await File.AppendAllTextAsync(filePath, "part");
            await Task.Delay(60);
        }

        FileTransferredEventArgs args = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(24, args.Size);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task Start_SubdirectoryCreated_RaisesNoEvents()
    {
        using var fixture = new MonitorFixture();

        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50));

        var received = new TaskCompletionSource<FileTransferredEventArgs>();
        monitor.FileReceived += (_, e) => received.TrySetResult(e);

        monitor.Start(fixture.Folder);

        Directory.CreateDirectory(Path.Combine(fixture.Folder, "subfolder"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Start_FileGrows_RaisesProgressBeforeReceived()
    {
        using var fixture = new MonitorFixture();

        // One stable sample is enough so the file is reported quickly.
        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50),
            stableSamples: 1);

        var progress = new TaskCompletionSource<FileProgressEventArgs>();
        var received = new TaskCompletionSource<FileTransferredEventArgs>();
        monitor.FileProgress += (_, e) =>
        {
            if (e.FileName == "photo.jpg")
            {
                progress.TrySetResult(e);
            }
        };
        monitor.FileReceived += (_, e) => received.TrySetResult(e);

        monitor.Start(fixture.Folder);

        string filePath = Path.Combine(fixture.Folder, "photo.jpg");
        await File.WriteAllBytesAsync(filePath, new byte[4096]);

        FileProgressEventArgs progressArgs = await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4096, progressArgs.BytesCopied);
        Assert.False(progressArgs.IsAppCopy);

        FileTransferredEventArgs receivedArgs = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("photo.jpg", receivedArgs.FileName);
    }

    [Fact]
    public async Task Start_AppCopy_RaisesProgressWithIsAppCopyTrue()
    {
        using var fixture = new MonitorFixture();

        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance,
            TimeSpan.FromMilliseconds(50),
            stableSamples: 1);

        var progress = new TaskCompletionSource<FileProgressEventArgs>();
        monitor.FileProgress += (_, e) =>
        {
            if (e.FileName == "notes.txt")
            {
                progress.TrySetResult(e);
            }
        };

        monitor.Start(fixture.Folder);

        string filePath = Path.Combine(fixture.Folder, "notes.txt");
        await File.WriteAllTextAsync(filePath, "hello");
        monitor.RegisterAppCopiedFile("notes.txt");

        FileProgressEventArgs progressArgs = await progress.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(progressArgs.IsAppCopy);
    }

    [Fact]
    public void Start_MissingFolder_Throws()
    {
        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance);

        Assert.Throws<DirectoryNotFoundException>(
            () => monitor.Start(@"C:\definitely\not\here"));
    }

    [Fact]
    public void Stop_ThenStart_WorksAgain()
    {
        using var fixture = new MonitorFixture();
        using var monitor = new FileSystemTransferMonitor(
            NullLogger<FileSystemTransferMonitor>.Instance);

        monitor.Start(fixture.Folder);
        monitor.Stop();
        monitor.Start(fixture.Folder);

        Assert.True(true);
    }

    private sealed class MonitorFixture : IDisposable
    {
        public string Folder { get; }

        public MonitorFixture()
        {
            Folder = Path.Combine(Path.GetTempPath(), "KinoShareMonitorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public void Dispose()
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
    }
}
