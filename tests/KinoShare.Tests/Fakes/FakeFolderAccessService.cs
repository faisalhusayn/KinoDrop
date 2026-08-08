namespace KinoShare.Tests.Fakes;

using KinoShare.Core.Abstractions;

/// <summary>
/// Stand-in for <see cref="IFolderAccessService"/> used by unit tests.
/// </summary>
internal sealed class FakeFolderAccessService : IFolderAccessService
{
    public List<(string FolderPath, string AccountName)> Grants { get; } = [];

    public Exception? GrantException { get; set; }

    public Task GrantReadWriteAsync(string folderPath, string accountName, CancellationToken cancellationToken = default)
    {
        if (GrantException is not null)
        {
            throw GrantException;
        }

        Grants.Add((folderPath, accountName));
        return Task.CompletedTask;
    }
}
