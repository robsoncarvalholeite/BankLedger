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
        
        var affectedRows = await connection.ExecuteAsync(
            @"UPDATE BalanceSnapshots
              SET BalanceCents = @BalanceCents,
                  LastTransactionId = @LastTransactionId,
                  Sequence = Sequence + 1,
                  CreatedAt = @CreatedAt
              WHERE AccountNumber = @AccountNumber AND Sequence = @ExpectedSequence",
            new
            {
                AccountNumber = snapshot.AccountNumber,
                BalanceCents = (long)(snapshot.Balance.Amount * 100),
                LastTransactionId = snapshot.LastTransactionId,
                ExpectedSequence = expectedSequence,
                CreatedAt = DateTime.UtcNow.ToString("o")
            });

        if (affectedRows == 0)
        {
            return false;
        }

        return true;
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