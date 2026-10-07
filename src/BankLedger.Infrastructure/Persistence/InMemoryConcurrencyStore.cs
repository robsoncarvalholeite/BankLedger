using BankLedger.Domain.Ports;
using System.Collections.Concurrent;
using System.Threading;

namespace BankLedger.Infrastructure.Persistence;

public sealed class InMemoryConcurrencyStore : IConcurrencyStore
{
    private readonly ConcurrentDictionary<string, long> _versions = new();

    public Task<long> NextAsync(string accountNumber, CancellationToken ct)
    {
        var newVersion = _versions.AddOrUpdate(accountNumber, 1, (_, v) => v + 1);
        return Task.FromResult(newVersion);
    }
}