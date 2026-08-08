namespace KinoShare.Tests.Fakes;

using KinoShare.Core.Abstractions;
using KinoShare.Core.Models;

/// <summary>
/// In-memory stand-in for <see cref="ISmbShareService"/> used by unit tests.
/// </summary>
internal sealed class FakeSmbShareService : ISmbShareService
{
    public List<ShareRequest> CreatedRequests { get; } = [];

    public List<string> RemovedShares { get; } = [];

    public Exception? CreateException { get; set; }

    public int CreateFailureCount { get; set; } = 1;

    public Exception? RemoveException { get; set; }

    public Task<ShareInfo> CreateShareAsync(ShareRequest request, CancellationToken cancellationToken = default)
    {
        if (CreateException is not null && CreateFailureCount > 0)
        {
            CreateFailureCount--;
            throw CreateException;
        }

        CreatedRequests.Add(request);
        return Task.FromResult(new ShareInfo(request.ShareName, request.FolderPath, $@"\\TESTPC\{request.ShareName}"));
    }

    public Task RemoveShareAsync(string shareName, CancellationToken cancellationToken = default)
    {
        if (RemoveException is not null)
        {
            throw RemoveException;
        }

        RemovedShares.Add(shareName);
        return Task.CompletedTask;
    }
}
