using ReleaseNotes.Application.Interfaces;
using System.Collections.Concurrent;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class InMemoryLockService : IDistributedLockService
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> Locks = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (Locks.TryGetValue(key, out var expiresAt) && expiresAt > now)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Locks[key] = now.Add(ttl);
        return Task.FromResult<IAsyncDisposable?>(new InMemoryKeyReleaser(key));
    }

    private sealed class InMemoryKeyReleaser(string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Locks.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
