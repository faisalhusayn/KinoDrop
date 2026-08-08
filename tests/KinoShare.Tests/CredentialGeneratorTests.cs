namespace KinoShare.Tests;

using KinoShare.Core.Security;
using Xunit;

/// <summary>
/// Tests for <see cref="RandomUserCredentialGenerator"/>.
/// </summary>
public class CredentialGeneratorTests
{
    private readonly RandomUserCredentialGenerator _generator = new();

    [Fact]
    public void GeneratePassword_IsEightCharactersLong()
    {
        string password = _generator.GeneratePassword();

        Assert.Equal(8, password.Length);
    }

    [Fact]
    public void GeneratePassword_IsEasyToType()
    {
        for (int i = 0; i < 50; i++)
        {
            string password = _generator.GeneratePassword();

            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.All(password, static character => Assert.True(char.IsLower(character) || char.IsDigit(character)));
        }
    }

    [Fact]
    public void GeneratePassword_ProducesUniquePasswords()
    {
        HashSet<string> seen = [];
        int duplicates = 0;

        for (int i = 0; i < 1000; i++)
        {
            if (!seen.Add(_generator.GeneratePassword()))
            {
                duplicates++;
            }
        }

        Assert.Equal(0, duplicates);
    }

    [Fact]
    public void GeneratePassword_OmitsAmbiguousCharacters()
    {
        string password = _generator.GeneratePassword();

        Assert.DoesNotContain("0", password);
        Assert.DoesNotContain("1", password);
        Assert.DoesNotContain("l", password);
        Assert.DoesNotContain("i", password);
        Assert.DoesNotContain("o", password);
    }
}
