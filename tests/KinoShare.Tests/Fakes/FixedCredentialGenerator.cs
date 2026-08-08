namespace KinoShare.Tests.Fakes;

using KinoShare.Core.Abstractions;

/// <summary>
/// Stand-in for <see cref="IUserCredentialGenerator"/> that returns a fixed password.
/// </summary>
internal sealed class FixedCredentialGenerator : IUserCredentialGenerator
{
    public string Password { get; set; } = "TestPassword1!";

    public string GeneratePassword() => Password;
}
