namespace KinoShare.Infrastructure.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;
using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stores transfer history as JSON in <c>%LOCALAPPDATA%\KinoShare\Settings\history.json</c>.
/// Writes are atomic (temp file + rename) and serialized so concurrent saves
/// cannot corrupt the file; the list is capped so it cannot grow unbounded.
/// </summary>
public sealed class TransferHistoryService : ITransferHistoryService
{
    private const string HistoryFileName = "history.json";
    private const int MaxEntries = 200;

    private readonly ILogger<TransferHistoryService> _logger;
    private readonly string _historyFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferHistoryService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="settingsDirectory">Optional override of the settings directory (used by tests).</param>
    public TransferHistoryService(ILogger<TransferHistoryService> logger, string? settingsDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        string directory = settingsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                WorkspaceInfo.RootFolderName,
                WorkspaceInfo.SettingsFolderName);
        _historyFilePath = Path.Combine(directory, HistoryFileName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransferRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                string json = await File.ReadAllTextAsync(_historyFilePath, cancellationToken);
                var records = JsonSerializer.Deserialize<List<TransferRecord>>(json);
                if (records is not null)
                {
                    return records;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(exception, "Failed to read transfer history from {HistoryFile}; starting empty.", _historyFilePath);
        }

        return [];
    }

    /// <inheritdoc />
    public async Task AddAsync(TransferRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<TransferRecord> existing = await LoadAsync(cancellationToken);
            var updated = new List<TransferRecord>(MaxEntries) { record };
            updated.AddRange(existing.Take(MaxEntries - 1));

            string directory = Path.GetDirectoryName(_historyFilePath)
                ?? throw new InvalidOperationException("History path has no directory.");
            Directory.CreateDirectory(directory);

            string tempFile = _historyFilePath + ".tmp";
            string json = JsonSerializer.Serialize(updated, JsonSerializerOptions);
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);
            File.Move(tempFile, _historyFilePath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
