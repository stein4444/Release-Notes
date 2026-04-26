using Microsoft.Extensions.Caching.Distributed;
using ReleaseNotes.Application.Interfaces;
using System.Collections.Concurrent;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class RedisLockService(IDistributedCache cache) : IDistributedLockService
{
    private static readonly ConcurrentDictionary<string, byte> InMemoryLocks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await cache.GetStringAsync(key, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return null;
            }

            await cache.SetStringAsync(key, "1", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
            return new CacheKeyReleaser(cache, key);
        }
        catch
        {
            // Fallback for local/dev mode when Redis is unavailable.
            if (!InMemoryLocks.TryAdd(key, 1))
            {
                return null;
            }

            return new InMemoryKeyReleaser(key);
        }
    }

    private sealed class CacheKeyReleaser(IDistributedCache cache, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await cache.RemoveAsync(key);
    }

    private sealed class InMemoryKeyReleaser(string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            InMemoryLocks.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
