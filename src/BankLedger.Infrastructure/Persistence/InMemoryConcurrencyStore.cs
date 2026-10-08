using BankLedger.Domain.Ports;
using Dapper;
using System.Collections.Concurrent;
using System.Threading;

namespace BankLedger.Infrastructure.Persistence;

public sealed class InMemoryConcurrencyStore : IConcurrencyStore
{
    private readonly ConcurrentDictionary<string, long> _versions = new();

    public InMemoryConcurrencyStore(SqliteConnectionFactory connectionFactory)
    {
        var sql = """
            SELECT 
                t.account_id as AccountId,
                MAX(t.occ_version) as OccVersion
            FROM transactions t
            GROUP BY t.account_id;
            """;
        connectionFactory.CreateConnection()
        .Query<(string AccountId, long OccVersion)>(sql)
        .ToList()
        .ForEach(row => _versions[row.AccountId] = row.OccVersion);
    }

    public Task<long> NextAsync(string accountNumber, CancellationToken ct)
    {
        var newVersion = _versions.AddOrUpdate(accountNumber, 1, (_, v) => v + 1);
        return Task.FromResult(newVersion);
    }
}
