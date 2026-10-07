using Dapper;
using BankLedger.Application.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Exceptions;
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

    public async Task<BalanceSnapshot?> GetAsync(string accountNumber, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<SnapshotRow>(
            "SELECT AccountNumber, BalanceCents, LastTransactionId, Sequence, CreatedAt FROM BalanceSnapshots WHERE AccountNumber = @AccountNumber",
            new { AccountNumber = accountNumber });

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<string>> GetAccountsWithNewTransactionsAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var accounts = await connection.QueryAsync<string>(
            @"SELECT DISTINCT t.AccountNumber
              FROM Transactions t
              LEFT JOIN BalanceSnapshots s ON t.AccountNumber = s.AccountNumber
              WHERE s.AccountNumber IS NULL OR t.Id > s.LastTransactionId");

        return accounts.ToList();
    }

    public async Task<bool> UpdateAsync(BalanceSnapshot snapshot, long expectedSequence, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        
        var newSequence = expectedSequence == 0 ? 0 : expectedSequence + 1;
        
        var affectedRows = await connection.ExecuteAsync(
            @"INSERT INTO BalanceSnapshots (AccountNumber, BalanceCents, LastTransactionId, Sequence, CreatedAt)
              VALUES (@AccountNumber, @BalanceCents, @LastTransactionId, @NewSequence, @CreatedAt)
              ON CONFLICT(AccountNumber) DO UPDATE SET
                  BalanceCents = excluded.BalanceCents,
                  LastTransactionId = excluded.LastTransactionId,
                  Sequence = CASE 
                      WHEN @ExpectedSequence = 0 THEN BalanceSnapshots.Sequence + 1
                      ELSE excluded.Sequence
                  END,
                  CreatedAt = excluded.CreatedAt
              WHERE (@ExpectedSequence = 0 AND BalanceSnapshots.Sequence = 0) OR BalanceSnapshots.Sequence = @ExpectedSequence",
            new
            {
                AccountNumber = snapshot.AccountNumber,
                BalanceCents = (long)(snapshot.Balance.Amount * 100),
                LastTransactionId = snapshot.LastTransactionId,
                NewSequence = newSequence,
                ExpectedSequence = expectedSequence,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });

        return affectedRows > 0;
    }

    private sealed record SnapshotRow(string AccountNumber, long BalanceCents, long LastTransactionId, long Sequence, string CreatedAt)
    {
        public BalanceSnapshot ToDomain()
        {
            return BalanceSnapshot.Restore(
                AccountNumber,
                new Money(BalanceCents / 100m),
                LastTransactionId,
                Sequence,
                DateTime.Parse(CreatedAt));
        }
    }
}