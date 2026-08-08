namespace KinoShare.Infrastructure.Settings;

using System.Security.Cryptography;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stores the shared connection password DPAPI-encrypted (current-user scope)
/// in <c>%LOCALAPPDATA%\KinoShare\Settings\credential.bin</c>. The value can
/// only be decrypted by the same Windows user profile that wrote it; an
/// unreadable or missing file simply means no remembered credential yet.
/// </summary>
public sealed class DeviceCredentialStore : IDeviceCredentialStore
{
    private const string CredentialFileName = "credential.bin";

    private static readonly byte[] Entropy = "KinoShare"u8.ToArray();

    private readonly ILogger<DeviceCredentialStore> _logger;
    private readonly string _credentialFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCredentialStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="settingsDirectory">Optional override of the settings directory (used by tests).</param>
    public DeviceCredentialStore(ILogger<DeviceCredentialStore> logger, string? settingsDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        string directory = settingsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceInfo.RootFolderName,
                WorkspaceInfo.SettingsFolderName);
        _credentialFilePath = Path.Combine(directory, CredentialFileName);
    }

    /// <inheritdoc />
    public Task<string?> GetPasswordAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_credentialFilePath))
            {
                return Task.FromResult<string?>(null);
            }

            byte[] protectedBytes = File.ReadAllBytes(_credentialFilePath);
            if (protectedBytes.Length == 0)
            {
                return Task.FromResult<string?>(null);
            }

            byte[] bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(bytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning(exception, "Failed to read the remembered credential from {CredentialFile}; treating it as unset.", _credentialFilePath);
            return Task.FromResult<string?>(null);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetOrCreatePasswordAsync(Func<string> passwordFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passwordFactory);

        string? existing = await GetPasswordAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        string generated = passwordFactory();
        await SetPasswordAsync(generated, cancellationToken);
        return generated;
    }

    /// <inheritdoc />
    public async Task SetPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length == 0)
        {
            throw new ArgumentException("The password must not be empty.", nameof(password));
        }

        byte[] protectedBytes = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(password),
            Entropy,
            DataProtectionScope.CurrentUser);

        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(_credentialFilePath)
            ?? throw new InvalidOperationException("Credential path has no directory.");
        Directory.CreateDirectory(directory);

        string tempFile = _credentialFilePath + ".tmp";
        await File.WriteAllBytesAsync(tempFile, protectedBytes, cancellationToken);
        File.Move(tempFile, _credentialFilePath, overwrite: true);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_credentialFilePath))
        {
            File.Delete(_credentialFilePath);
        }

        return Task.CompletedTask;
    }
}
