using Dapper;
using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace BankLedger.Infrastructure.Persistence;

public sealed class SnapshotRepository : ISnapshotRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SnapshotRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BalanceSnapshot?> GetLastAsync(Guid accountId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<SnapshotRow>(
            @"SELECT 
                account_id as AccountId,
                balance_in_cents as BalanceInCents,
                last_transaction_id as LastTransactionId,
                occ_version as OccVersion,
                created_at as CreatedAt
            FROM balance_snapshots
            WHERE account_id = @AccountId
            ORDER BY occ_version DESC
            LIMIT 1",
            new { AccountId = accountId.ToString() });

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Guid>> GetAccountsWithNewTransactionsAsync(CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        var accountIds = await connection.QueryAsync<string>(
            @"
            SELECT a.id
            FROM accounts a,
                transactions t
            LEFT JOIN
            (SELECT bs.account_id,
                    max(bs.last_transaction_id) AS last_transaction_id
            FROM balance_snapshots bs
            GROUP BY bs.account_id) AS ls ON a.id = ls.account_id
            WHERE t.account_id = a.id
            AND (t.id > ls.last_transaction_id
                OR ls.last_transaction_id IS NULL)
            GROUP BY a.id
            LIMIT 50
        ");

        return accountIds.Select(Guid.Parse).ToList();
    }

    public async Task<BalanceSnapshot> CreateAsync(BalanceSnapshot snapshot, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            @"INSERT INTO balance_snapshots (account_id, balance_in_cents, last_transaction_id, occ_version, created_at)
              VALUES (@AccountId, @BalanceInCents, @LastTransactionId, @OccVersion, @CreatedAt)",
            new
            {
                AccountId = snapshot.AccountId.ToString(),
                BalanceInCents = (long)(snapshot.Balance.Amount * 100),
                LastTransactionId = snapshot.LastTransactionId.ToString(),
                OccVersion = snapshot.OccVersion,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });

        return snapshot;
    }

    private sealed record SnapshotRow(string AccountId, long BalanceInCents, string LastTransactionId, long OccVersion, string CreatedAt)
    {
        public BalanceSnapshot ToDomain()
        {
            return BalanceSnapshot.Restore(
                Guid.Parse(AccountId),
                new Money(BalanceInCents / 100m),
                Guid.Parse(LastTransactionId),
                OccVersion,
                DateTime.Parse(CreatedAt));
        }
    }
}
