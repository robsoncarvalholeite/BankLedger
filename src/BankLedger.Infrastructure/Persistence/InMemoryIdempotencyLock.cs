using BankLedger.Domain.Ports;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace BankLedger.Infrastructure.Persistence;

public sealed class InMemoryIdempotencyLock : IdempotencyLock
{
    private readonly ConcurrentBag<Guid> _keys = new();

    public Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken ct)
    {
        return Task.FromResult(_keys.Contains(idempotencyKey));
    }

    public Task AddAsync(Guid idempotencyKey, CancellationToken ct)
    {
        _keys.Add(idempotencyKey);
        return Task.CompletedTask;
    }
}
