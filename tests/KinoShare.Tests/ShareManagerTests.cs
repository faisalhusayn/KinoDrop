namespace KinoShare.Tests;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Core.Services;
using KinoShare.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Tests for <see cref="ShareManager"/> session orchestration.
/// </summary>
public class ShareManagerTests
{
    private readonly FakeSmbShareService _smbService = new();
    private readonly FakeUserAccountService _userService = new();
    private readonly FakeFolderAccessService _folderService = new();
    private readonly FixedCredentialGenerator _credentialGenerator = new();

    private readonly ShareManager _manager;

    public ShareManagerTests()
    {
        _manager = new ShareManager(
            _smbService,
            _userService,
            _credentialGenerator,
            _folderService,
            NullLogger<ShareManager>.Instance);
    }

    [Fact]
    public async Task CreateShareSession_ValidRequest_CreatesUserShareAndGrant()
    {
        string folder = CreateTempFolder();

        ShareSession session = await _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        Assert.Equal(TemporaryUser.DefaultUsername, session.User.Username);
        Assert.Equal(_credentialGenerator.Password, session.User.Password);
        Assert.Equal("test-share", session.Share.Name);

        Assert.Single(_userService.CreatedUsers);
        Assert.Equal(TemporaryUser.DefaultUsername, _userService.CreatedUsers[0].Username);

        Assert.Single(_smbService.CreatedRequests);
        Assert.Equal(TemporaryUser.DefaultUsername, _smbService.CreatedRequests[0].GrantAccessTo);

        Assert.Single(_folderService.Grants);
        Assert.Equal((folder, TemporaryUser.DefaultUsername), _folderService.Grants[0]);
    }

    [Fact]
    public async Task CreateShareSession_MissingFolder_DoesNotCreateUser()
    {
        var request = new ShareRequest(@"C:\definitely\not\here", "test-share");

        var exception = await Assert.ThrowsAsync<FolderNotFoundException>(
            () => _manager.CreateShareSessionAsync(request));

        Assert.Equal(request.FolderPath, exception.FolderPath);
        Assert.Empty(_userService.CreatedUsers);
        Assert.Empty(_smbService.CreatedRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("CON")]
    [InlineData("trailing ")]
    public async Task CreateShareSession_InvalidShareName_ThrowsAndDoesNotCreateUser(string shareName)
    {
        string folder = CreateTempFolder();

        await Assert.ThrowsAsync<InvalidShareNameException>(
            () => _manager.CreateShareSessionAsync(new ShareRequest(folder, shareName)));

        Assert.Empty(_userService.CreatedUsers);
        Assert.Empty(_smbService.CreatedRequests);
    }

    [Fact]
    public async Task CreateShareSession_LeftoverShare_RemovesItAndRetries()
    {
        string folder = CreateTempFolder();
        _smbService.CreateException = new ShareAlreadyExistsException("test-share");

        ShareSession session = await _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        Assert.Equal("test-share", session.Share.Name);
        Assert.Equal(["test-share"], _smbService.RemovedShares);
        Assert.Single(_smbService.CreatedRequests);
        Assert.Single(_userService.CreatedUsers);
    }

    [Fact]
    public async Task CreateShareSession_ShareCreationFails_DeletesUserAndPropagates()
    {
        string folder = CreateTempFolder();
        _smbService.CreateException = new ShareOperationFailedException("create", "boom");
        _smbService.CreateFailureCount = 2;

        await Assert.ThrowsAsync<ShareOperationFailedException>(
            () => _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share")));

        Assert.Single(_userService.CreatedUsers);
        Assert.Equal([TemporaryUser.DefaultUsername], _userService.DeletedUsers);
        Assert.Empty(_smbService.RemovedShares);
    }

    [Fact]
    public async Task CreateShareSession_FolderGrantFails_RemovesShareAndDeletesUser()
    {
        string folder = CreateTempFolder();
        _folderService.GrantException = new ShareOperationFailedException("grant access to", "boom");

        await Assert.ThrowsAsync<ShareOperationFailedException>(
            () => _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share")));

        Assert.Equal(["test-share"], _smbService.RemovedShares);
        Assert.Equal([TemporaryUser.DefaultUsername], _userService.DeletedUsers);
    }

    [Fact]
    public async Task CreateShareSession_UserCreationFails_PropagatesWithoutSideEffects()
    {
        string folder = CreateTempFolder();
        _userService.CreateException = new UserAccountOperationFailedException("create", "boom");

        await Assert.ThrowsAsync<UserAccountOperationFailedException>(
            () => _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share")));

        Assert.Empty(_smbService.CreatedRequests);
        Assert.Empty(_userService.DeletedUsers);
    }

    [Fact]
    public async Task RemoveShareSession_RemovesShareThenDeletesUser()
    {
        string folder = CreateTempFolder();
        ShareSession session = await _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        await _manager.RemoveShareSessionAsync(session);

        Assert.Equal(["test-share"], _smbService.RemovedShares);
        Assert.Equal([TemporaryUser.DefaultUsername], _userService.DeletedUsers);
    }

    [Fact]
    public async Task RemoveShareSession_UserDeletionFails_PropagatesAfterShareRemoval()
    {
        string folder = CreateTempFolder();
        ShareSession session = await _manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));
        _userService.DeleteException = new UserAccountOperationFailedException("delete", "in use");

        await Assert.ThrowsAsync<UserAccountOperationFailedException>(
            () => _manager.RemoveShareSessionAsync(session));

        Assert.Equal(["test-share"], _smbService.RemovedShares);
    }

    [Fact]
    public async Task CreateShareSession_WithStoredCredential_UsesSamePasswordEverySession()
    {
        string folder = CreateTempFolder();
        var store = new FakeCredentialStore();
        await store.SetPasswordAsync("user-chosen-pass");
        var manager = new ShareManager(
            _smbService,
            _userService,
            _credentialGenerator,
            _folderService,
            NullLogger<ShareManager>.Instance,
            credentialStore: store);

        ShareSession first = await manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));
        ShareSession second = await manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        Assert.Equal("user-chosen-pass", first.User.Password);
        Assert.Equal("user-chosen-pass", second.User.Password);
        Assert.Equal(0, store.FactoryCalls);
    }

    [Fact]
    public async Task CreateShareSession_WithEmptyStore_GeneratesAndPersistsOnce()
    {
        string folder = CreateTempFolder();
        var store = new FakeCredentialStore();
        var manager = new ShareManager(
            _smbService,
            _userService,
            _credentialGenerator,
            _folderService,
            NullLogger<ShareManager>.Instance,
            credentialStore: store);

        ShareSession first = await manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));
        ShareSession second = await manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        Assert.Equal(_credentialGenerator.Password, first.User.Password);
        Assert.Equal(first.User.Password, second.User.Password);
        Assert.Equal(store.StoredPassword, first.User.Password);
        Assert.Equal(1, store.FactoryCalls);
    }

    [Fact]
    public async Task CreateShareSession_StoreFails_FallsBackToGeneratedPassword()
    {
        string folder = CreateTempFolder();
        var manager = new ShareManager(
            _smbService,
            _userService,
            _credentialGenerator,
            _folderService,
            NullLogger<ShareManager>.Instance,
            credentialStore: new ThrowingCredentialStore());

        ShareSession session = await manager.CreateShareSessionAsync(new ShareRequest(folder, "test-share"));

        Assert.Equal(_credentialGenerator.Password, session.User.Password);
        Assert.Single(_userService.CreatedUsers);
    }

    /// <summary>A credential store whose every operation throws.</summary>
    private sealed class ThrowingCredentialStore : IDeviceCredentialStore
    {
        public Task<string?> GetPasswordAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public Task<string> GetOrCreatePasswordAsync(Func<string> passwordFactory, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "KinoShareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
