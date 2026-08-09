namespace KinoShare.Core.Services;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Exceptions;
using KinoShare.Core.Models;
using KinoShare.Core.Validation;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

/// <summary>
/// Coordinates the full lifecycle of a sharing session: provisioning a
/// temporary user, creating the share, granting folder access, and cleaning
/// everything up afterwards. Contains all business rules; knows nothing about
/// Windows internals.
/// </summary>
public sealed class ShareManager
{
    private readonly ISmbShareService _smbShareService;
    private readonly IUserAccountService _userAccountService;
    private readonly IUserCredentialGenerator _credentialGenerator;
    private readonly IFolderAccessService _folderAccessService;
    private readonly IFirewallService _firewallService;
    private readonly IDeviceCredentialStore? _credentialStore;
    private readonly ILogger<ShareManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareManager"/> class.
    /// </summary>
    public ShareManager(
        ISmbShareService smbShareService,
        IUserAccountService userAccountService,
        IUserCredentialGenerator credentialGenerator,
        IFolderAccessService folderAccessService,
        ILogger<ShareManager> logger,
        IFirewallService? firewallService = null,
        IDeviceCredentialStore? credentialStore = null)
    {
        _smbShareService = smbShareService ?? throw new ArgumentNullException(nameof(smbShareService));
        _userAccountService = userAccountService ?? throw new ArgumentNullException(nameof(userAccountService));
        _credentialGenerator = credentialGenerator ?? throw new ArgumentNullException(nameof(credentialGenerator));
        _folderAccessService = folderAccessService ?? throw new ArgumentNullException(nameof(folderAccessService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _firewallService = firewallService ?? NoOpFirewallService.Instance;
        _credentialStore = credentialStore;
    }

    /// <summary>
    /// Creates a complete sharing session: a temporary user, an SMB share
    /// granted to that user, and folder-level access for the user.
    /// </summary>
    /// <param name="request">The folder and share name to use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The share together with the temporary credentials.</returns>
    /// <exception cref="FolderNotFoundException">
    /// Thrown when the folder does not exist.
    /// </exception>
    /// <exception cref="InvalidShareNameException">
    /// Thrown when the share name is not valid.
    /// </exception>
    public async Task<ShareSession> CreateShareSessionAsync(ShareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch startupTimer = Stopwatch.StartNew();

        if (!Directory.Exists(request.FolderPath))
        {
            _logger.LogError("Share creation failed: folder {FolderPath} does not exist.", request.FolderPath);
            throw new FolderNotFoundException(request.FolderPath);
        }

        ShareNameValidator.Validate(request.ShareName);

        var passwordTimer = Stopwatch.StartNew();
        var user = new TemporaryUser(TemporaryUser.DefaultUsername, await ResolvePasswordAsync(cancellationToken));
        passwordTimer.Stop();

        _logger.LogInformation("Provisioning temporary user {Username} for share {ShareName} (credential: {ElapsedMs} ms).", user.Username, request.ShareName, passwordTimer.ElapsedMilliseconds);
        Stopwatch stageTimer = Stopwatch.StartNew();
        await _userAccountService.CreateTemporaryUserAsync(user, cancellationToken);
        stageTimer.Stop();
        _logger.LogInformation("Temporary user ready in {ElapsedMs} ms.", stageTimer.ElapsedMilliseconds);

        ShareInfo? share = null;
        Task firewallTask = AllowFirewallAsync(cancellationToken);

        try
        {
            var shareRequest = request with { GrantAccessTo = user.Username };

            _logger.LogInformation("Creating share {ShareName} for folder {FolderPath}.", shareRequest.ShareName, shareRequest.FolderPath);
            stageTimer.Restart();
            share = await CreateShareAsyncSelfHealingAsync(shareRequest, cancellationToken);
            stageTimer.Stop();
            _logger.LogInformation("SMB share ready in {ElapsedMs} ms.", stageTimer.ElapsedMilliseconds);

            _logger.LogInformation("Granting {Username} access to folder {FolderPath}.", user.Username, request.FolderPath);
            stageTimer.Restart();
            await _folderAccessService.GrantReadWriteAsync(request.FolderPath, user.Username, cancellationToken);
            stageTimer.Stop();
            _logger.LogInformation("Folder permissions ready in {ElapsedMs} ms.", stageTimer.ElapsedMilliseconds);

            stageTimer.Restart();
            await firewallTask;
            stageTimer.Stop();
            _logger.LogInformation("Firewall ready in {ElapsedMs} ms.", stageTimer.ElapsedMilliseconds);

            _logger.LogInformation(
                "Share {ShareName} created; reachable at {UncPath}; temporary user {Username}.",
                share.Name, share.UncPath, user.Username);

            startupTimer.Stop();
            _logger.LogInformation("Share startup completed in {ElapsedMs} ms.", startupTimer.ElapsedMilliseconds);
            return new ShareSession(share, user);
        }
        catch (Exception exception) when (exception is KinoShareException or OperationCanceledException)
        {
            _logger.LogError(exception, "Share session creation failed for {ShareName}; cleaning up.", request.ShareName);
            try
            {
                await firewallTask;
                await RestoreFirewallAsync(CancellationToken.None);
            }
            catch (Exception cleanupException) when (cleanupException is not OperationCanceledException)
            {
                _logger.LogWarning(cleanupException, "Failed to clean up the firewall rule after startup failure.");
            }
            await CleanupAfterFailureAsync(share, user);
            throw;
        }
    }

    /// <summary>
    /// Removes the share and deletes the temporary user for a session.
    /// </summary>
    /// <param name="session">The session to tear down.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RemoveShareSessionAsync(ShareSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        _logger.LogInformation("Removing share {ShareName}.", session.Share.Name);
        await _smbShareService.RemoveShareAsync(session.Share.Name, cancellationToken);

        _logger.LogInformation("Deleting temporary user {Username}.", session.User.Username);
        await _userAccountService.DeleteUserAsync(session.User.Username, cancellationToken);

        await RestoreFirewallAsync(cancellationToken);

        _logger.LogInformation("Share {ShareName} and user {Username} removed.", session.Share.Name, session.User.Username);
    }

    private async Task AllowFirewallAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _firewallService.AllowSmbInboundAsync(cancellationToken))
            {
                _logger.LogWarning(
                    "The SMB firewall rule could not be created. The phone may not connect on Public networks; " +
                    "allow 'File and Printer Sharing' manually if needed.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to create the SMB firewall rule; continuing without it.");
        }
    }

    private async Task RestoreFirewallAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _firewallService.RestoreSmbInboundAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to remove the SMB firewall rule; it will be replaced on the next session.");
        }
    }

    /// <summary>
    /// Resolves the password for a new session: the remembered credential
    /// when a store is configured (generating and persisting one on first
    /// use), otherwise a fresh random password.
    /// </summary>
    private async Task<string> ResolvePasswordAsync(CancellationToken cancellationToken)
    {
        if (_credentialStore is null)
        {
            return _credentialGenerator.GeneratePassword();
        }

        try
        {
            return await _credentialStore.GetOrCreatePasswordAsync(
                _credentialGenerator.GeneratePassword,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not read the remembered credential; using a session password instead.");
            return _credentialGenerator.GeneratePassword();
        }
    }

    /// <summary>A firewall service that does nothing; used when none is provided.</summary>
    private sealed class NoOpFirewallService : IFirewallService
    {
        public static NoOpFirewallService Instance { get; } = new();

        public Task<bool> AllowSmbInboundAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task RestoreSmbInboundAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsSmbInboundAllowedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private async Task<ShareInfo> CreateShareAsyncSelfHealingAsync(ShareRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _smbShareService.CreateShareAsync(request, cancellationToken);
        }
        catch (ShareAlreadyExistsException)
        {
            // A share with this name was left behind by an interrupted
            // session (e.g. the app was killed before cleanup). Remove it
            // and create the share fresh.
            _logger.LogWarning(
                "Share {ShareName} already exists (leftover); removing it and retrying.",
                request.ShareName);

            await _smbShareService.RemoveShareAsync(request.ShareName, cancellationToken);
            return await _smbShareService.CreateShareAsync(request, cancellationToken);
        }
    }

    private async Task CleanupAfterFailureAsync(ShareInfo? share, TemporaryUser user)
    {
        if (share is not null)
        {
            try
            {
                await _smbShareService.RemoveShareAsync(share.Name, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to remove share {ShareName} during rollback.", share.Name);
            }
        }

        try
        {
            await _userAccountService.DeleteUserAsync(user.Username, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to delete user {Username} during rollback.", user.Username);
        }
    }
}
