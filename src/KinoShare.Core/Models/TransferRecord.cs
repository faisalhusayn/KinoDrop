namespace KinoShare.Core.Models;

/// <summary>
/// A single completed transfer, persisted across sessions so the live feed
/// survives app restarts.
/// </summary>
/// <param name="Direction">"Received" or "Sent".</param>
/// <param name="FileName">The file name without its directory.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Timestamp">When the transfer completed.</param>
public sealed record TransferRecord(string Direction, string FileName, long Size, DateTime Timestamp);
