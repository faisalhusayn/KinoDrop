namespace KinoShare.Core.Models;

/// <summary>
/// The result of a complete sharing session: the share itself plus the
/// temporary user credentials clients should use to connect.
/// </summary>
/// <param name="Share">The created share.</param>
/// <param name="User">The temporary user for this session.</param>
public sealed record ShareSession(ShareInfo Share, TemporaryUser User);
