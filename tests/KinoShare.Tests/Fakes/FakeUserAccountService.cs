namespace KinoShare.Tests.Fakes;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;

/// <summary>
/// In-memory stand-in for <see cref="IUserAccountService"/> used by unit tests.
/// </summary>
internal sealed class FakeUserAccountService : IUserAccountService
{
    public List<TemporaryUser> CreatedUsers { get; } = [];

    public List<string> DeletedUsers { get; } = [];

    public Exception? CreateException { get; set; }

    public Exception? DeleteException { get; set; }

    public Task CreateTemporaryUserAsync(TemporaryUser user, CancellationToken cancellationToken = default)
    {
        if (CreateException is not null)
        {
            throw CreateException;
        }

        CreatedUsers.Add(user);
        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(string username, CancellationToken cancellationToken = default)
    {
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        DeletedUsers.Add(username);
        return Task.CompletedTask;
    }
}
