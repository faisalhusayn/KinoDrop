namespace KinoShare.Core.Validation;

using KinoShare.Core.Exceptions;

/// <summary>
/// Validates SMB share names against Windows naming rules.
/// </summary>
public static class ShareNameValidator
{
    private static readonly char[] InvalidCharacters = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Validates a share name and returns it unchanged.
    /// </summary>
    /// <param name="shareName">The share name to validate.</param>
    /// <returns>The validated share name.</returns>
    /// <exception cref="InvalidShareNameException">
    /// Thrown when the name violates Windows share naming rules.
    /// </exception>
    public static string Validate(string shareName)
    {
        if (string.IsNullOrWhiteSpace(shareName))
        {
            throw new InvalidShareNameException(shareName, "the name is empty.");
        }

        if (shareName.Length > 80)
        {
            throw new InvalidShareNameException(shareName, "the name is longer than 80 characters.");
        }

        if (shareName.IndexOfAny(InvalidCharacters) >= 0)
        {
            throw new InvalidShareNameException(shareName, "the name contains invalid characters.");
        }

        if (shareName.EndsWith(' ') || shareName.EndsWith('.'))
        {
            throw new InvalidShareNameException(shareName, "the name must not end with a space or a period.");
        }

        string baseName = shareName.TrimEnd('$');
        if (ReservedNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidShareNameException(shareName, "the name is reserved by Windows.");
        }

        return shareName;
    }
}
