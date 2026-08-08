namespace KinoShare.Tests;

using KinoShare.Core.Models;
using KinoShare.Infrastructure.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Tests for <see cref="WorkspaceService"/>.
/// </summary>
public class WorkspaceServiceTests
{
    [Fact]
    public async Task EnsureCreatedAsync_FirstRun_CreatesFullLayout()
    {
        (string root, string transfer) = TempPaths();

        WorkspaceInfo workspace = await new WorkspaceService(
                NullLogger<WorkspaceService>.Instance, root, transfer)
            .EnsureCreatedAsync();

        Assert.Equal(root, workspace.RootPath);
        Assert.Equal(transfer, workspace.TransferFolderPath);
        Assert.True(Directory.Exists(workspace.TransferFolderPath));
        Assert.True(Directory.Exists(workspace.TempPath));
        Assert.True(Directory.Exists(workspace.LogsPath));
        Assert.True(Directory.Exists(workspace.SettingsPath));

        // The transfer folder starts empty - the app creates it, it never
        // shares an existing directory's contents.
        Assert.Empty(Directory.GetFileSystemEntries(workspace.TransferFolderPath));
    }

    [Fact]
    public async Task EnsureCreatedAsync_SecondRun_IsIdempotent()
    {
        (string root, string transfer) = TempPaths();
        var service = new WorkspaceService(NullLogger<WorkspaceService>.Instance, root, transfer);

        WorkspaceInfo first = await service.EnsureCreatedAsync();
        WorkspaceInfo second = await service.EnsureCreatedAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task EnsureCreatedAsync_PreservesExistingFiles()
    {
        (string root, string transfer) = TempPaths();
        var service = new WorkspaceService(NullLogger<WorkspaceService>.Instance, root, transfer);

        WorkspaceInfo first = await service.EnsureCreatedAsync();
        string marker = Path.Combine(first.TransferFolderPath, "keep-me.txt");
        await File.WriteAllTextAsync(marker, "hello");

        await service.EnsureCreatedAsync();

        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void ResolveTransferFolder_Null_UsesDefaultUnderAppHome()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            WorkspaceInfo.RootFolderName,
            WorkspaceInfo.DefaultTransferFolderName);

        Assert.Equal(expected, WorkspaceService.ResolveTransferFolder(null));
        Assert.Equal(expected, WorkspaceService.ResolveTransferFolder("  "));
    }

    [Fact]
    public void ResolveTransferFolder_Location_CreatesKinoShareFolderInside()
    {
        string location = Path.Combine(Path.GetTempPath(), "MyTransfers");

        Assert.Equal(
            Path.Combine(location, WorkspaceInfo.RootFolderName),
            WorkspaceService.ResolveTransferFolder(location));
    }

    [Fact]
    public void ResolveTransferFolder_LocationEndingInKinoShare_UsedAsIs()
    {
        string location = Path.Combine(Path.GetTempPath(), WorkspaceInfo.RootFolderName);

        Assert.Equal(location, WorkspaceService.ResolveTransferFolder(location));
    }

    private static (string Root, string Transfer) TempPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "KinoShareWorkspaceTests", Guid.NewGuid().ToString("N"));
        return (root, Path.Combine(root, "Transfer"));
    }
}
