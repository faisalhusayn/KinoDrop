namespace KinoShare.Infrastructure.Settings;

using System.Text.Json;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stores settings as JSON in <c>%LOCALAPPDATA%\KinoShare\Settings\settings.json</c>.
/// Loads once and caches; writes are atomic (temp file + rename) and
/// serialized so concurrent saves cannot corrupt the file.
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private const string SettingsFileName = "settings.json";

    private readonly ILogger<AppSettingsService> _logger;
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AppSettings? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppSettingsService"/> class.
    /// </summary>
    public AppSettingsService(ILogger<AppSettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            WorkspaceInfo.RootFolderName);
        _settingsFilePath = Path.Combine(root, WorkspaceInfo.SettingsFolderName, SettingsFileName);
    }

    /// <inheritdoc />
    public Task<string?> GetTransferFolderLocationAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = Load();
        return Task.FromResult(settings.TransferFolderLocation);
    }

    /// <inheritdoc />
    public async Task SetTransferFolderLocationAsync(string? location, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            AppSettings settings = Load();
            settings.TransferFolderLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
            await SaveAsync(settings, cancellationToken);
            _cache = settings;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<string?> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = Load();
        return Task.FromResult(settings.Theme);
    }

    /// <inheritdoc />
    public async Task SetThemeAsync(string? theme, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            AppSettings settings = Load();
            settings.Theme = string.IsNullOrWhiteSpace(theme) ? null : theme.Trim();
            await SaveAsync(settings, cancellationToken);
            _cache = settings;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private AppSettings Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                _cache = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                return _cache;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Failed to read settings from {SettingsFile}; using defaults.", _settingsFilePath);
        }

        _cache = new AppSettings();
        return _cache;
    }

    private async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings path has no directory.");

        Directory.CreateDirectory(directory);

        string tempFile = _settingsFilePath + ".tmp";
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(tempFile, json, cancellationToken);
        File.Move(tempFile, _settingsFilePath, overwrite: true);
    }

    private sealed class AppSettings
    {
        public string? TransferFolderLocation { get; set; }

        public string? Theme { get; set; }
    }
}
