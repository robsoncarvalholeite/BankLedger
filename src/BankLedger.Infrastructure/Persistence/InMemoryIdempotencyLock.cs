using BankLedger.Domain.Ports;
using System.Collections.Concurrent;

namespace BankLedger.Infrastructure.Persistence;

public sealed class InMemoryIdempotencyLock : IdempotencyLock
{
    private readonly ConcurrentDictionary<Guid, byte> _keys = new();

    public Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken ct)
    {
        return Task.FromResult(_keys.ContainsKey(idempotencyKey));
    }

    public Task AddAsync(Guid idempotencyKey, CancellationToken ct)
    {
        _keys.TryAdd(idempotencyKey, 0);
        return Task.CompletedTask;
    }
}