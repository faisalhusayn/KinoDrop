namespace KinoShare.Tests;

using KinoShare.Core.Exceptions;
using KinoShare.Core.Validation;
using Xunit;

/// <summary>
/// Tests for share-name validation and sanitization.
/// </summary>
public class ValidationTests
{
    [Theory]
    [InlineData("photos")]
    [InlineData("My-Share")]
    [InlineData("videos_2026")]
    public void Validate_AcceptableNames_Passes(string shareName)
    {
        Assert.Equal(shareName, ShareNameValidator.Validate(shareName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("LPT1")]
    [InlineData("nul")]
    [InlineData("ends-with-space ")]
    [InlineData("ends-with-dot.")]
    public void Validate_InvalidNames_Throws(string shareName)
    {
        Assert.Throws<InvalidShareNameException>(() => ShareNameValidator.Validate(shareName));
    }

    [Theory]
    [InlineData(@"C:\Users\Faisal\My Photos", "my-photos")]
    [InlineData(@"C:\Users\Faisal\Photos", "photos")]
    [InlineData(@"C:\Users\Faisal\Log Footage", "log-footage")]
    [InlineData(@"D:\", "Share")]
    public void Sanitizer_FromFolderPath_ProducesValidName(string folderPath, string expected)
    {
        Assert.Equal(expected, ShareNameSanitizer.FromFolderPath(folderPath));
    }
}
