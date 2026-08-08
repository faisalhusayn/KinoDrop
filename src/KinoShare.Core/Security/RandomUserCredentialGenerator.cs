namespace KinoShare.Core.Security;

using System.Security.Cryptography;
using KinoShare.Core.Abstractions;

/// <summary>
/// Generates passwords that are easy to type on a phone: eight characters,
/// lowercase letters and digits only, no symbols, no ambiguous characters.
/// This machine does not enforce Windows password complexity, so plain
/// passwords are accepted while remaining much more pleasant to enter on an
/// iPhone keyboard.
/// </summary>
public sealed class RandomUserCredentialGenerator : IUserCredentialGenerator
{
    private const int Length = 8;

    // No i, l, o (ambiguous with 1/0), no 0, 1.
    private const string LowerCase = "abcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string All = LowerCase + Digits;

    /// <inheritdoc />
    public string GeneratePassword()
    {
        // Ensure at least one letter and one digit, then fill the rest
        // randomly and shuffle with a crypto RNG.
        var password = new char[Length];
        password[0] = RandomChar(LowerCase);
        password[1] = RandomChar(Digits);

        for (int i = 2; i < Length; i++)
        {
            password[i] = RandomChar(All);
        }

        Shuffle(password);

        return new string(password);
    }

    private static char RandomChar(string alphabet)
    {
        byte[] buffer = RandomNumberGenerator.GetBytes(sizeof(uint));
        uint value = BitConverter.ToUInt32(buffer) % (uint)alphabet.Length;
        return alphabet[(int)value];
    }

    private static void Shuffle(char[] values)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(0, i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
