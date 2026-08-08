namespace KinoShare.Core.Abstractions;

/// <summary>
/// Generates strong random passwords for temporary users.
/// </summary>
public interface IUserCredentialGenerator
{
    /// <summary>
    /// Generates a random password satisfying Windows default complexity rules
    /// (at least one upper-case, lower-case, digit and symbol; long enough to be strong).
    /// </summary>
    /// <returns>A new random password.</returns>
    string GeneratePassword();
}
