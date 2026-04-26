namespace ReleaseNotes.Application.Interfaces;

public interface IDistributedLockService
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}
