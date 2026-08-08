namespace KinoShare.Core.Models;

/// <summary>
/// A request to create an SMB share for a local folder.
/// </summary>
/// <param name="FolderPath">The absolute path of the folder to share.</param>
/// <param name="ShareName">The name of the share as seen on the network.</param>
/// <param name="GrantAccessTo">
/// The local account granted full access at the share level.
/// When <c>null</c>, access is granted to Everyone.
/// </param>
public sealed record ShareRequest(string FolderPath, string ShareName, string? GrantAccessTo = null);
