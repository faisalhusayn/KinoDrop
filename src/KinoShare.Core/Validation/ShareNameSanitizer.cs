namespace KinoShare.Core.Validation;

using System.Text;

/// <summary>
/// Derives a valid SMB share name from a folder path.
/// </summary>
public static class ShareNameSanitizer
{
    private const string DefaultShareName = "Share";

    /// <summary>
    /// Derives a share name from the last segment of a folder path, replacing
    /// invalid and whitespace characters with hyphens.
    /// </summary>
    /// <param name="folderPath">The folder path to derive the name from.</param>
    /// <returns>A valid, non-empty share name.</returns>
    public static string FromFolderPath(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.IsNullOrWhiteSpace(folderName))
        {
            return DefaultShareName;
        }

        var builder = new StringBuilder(folderName.Length);

        foreach (char character in folderName)
        {
            if (char.IsLetterOrDigit(character) || character == '-')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string result = builder.ToString().Trim('-');

        try
        {
            ShareNameValidator.Validate(result);
        }
        catch (Exceptions.InvalidShareNameException)
        {
            return DefaultShareName;
        }

        return result;
    }
}
