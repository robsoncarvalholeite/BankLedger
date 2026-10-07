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

    public async Task<BalanceSnapshot?> GetAsync(Guid accountId, CancellationToken cancellationToken)
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
              WHERE account_id = @AccountId",
            new { AccountId = accountId.ToString() });

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Guid>> GetAccountsWithNewTransactionsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var accountIds = await connection.QueryAsync<string>(
            @"SELECT DISTINCT t.account_id
              FROM transactions t
              LEFT JOIN balance_snapshots s ON t.account_id = s.account_id
              WHERE s.account_id IS NULL OR t.id > s.last_transaction_id");

        return accountIds.Select(Guid.Parse).ToList();
    }

    public async Task<BalanceSnapshot> CreateAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken)
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