namespace KinoShare.Core.Models;

/// <summary>
/// Describes an existing SMB share.
/// </summary>
/// <param name="Name">The share name as seen on the network.</param>
/// <param name="FolderPath">The local folder the share points at.</param>
/// <param name="UncPath">The UNC path clients use to reach the share, e.g. <c>\\PC\Share</c>.</param>
public sealed record ShareInfo(string Name, string FolderPath, string UncPath);
