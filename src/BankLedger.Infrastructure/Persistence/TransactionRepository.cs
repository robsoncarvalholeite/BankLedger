using Dapper;
using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using Microsoft.Data.Sqlite;
using BankLedger.Domain.Exceptions;

namespace BankLedger.Infrastructure.Persistence;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public TransactionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Money> GetBalanceDeltaAsync(string accountNumber, long lastTransactionId, DateTime? until, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = "SELECT AmountCents, Type FROM Transactions WHERE AccountNumber = @AccountNumber AND Id > @LastTransactionId";
        dynamic parameters = new { AccountNumber = accountNumber, LastTransactionId = lastTransactionId };

        if (until.HasValue)
        {
            sql += " AND CreatedAt <= @Until";
            parameters = new { AccountNumber = accountNumber, LastTransactionId = lastTransactionId, Until = until.Value.ToString("o") };
        }

        var rows = await connection.QueryAsync<BalanceDeltaRow>(sql, (object)parameters);

        var balance = new Money(0);
        foreach (var row in rows)
        {
            var amount = new Money(row.AmountCents / 100m);
            balance = row.Type switch
            {
                "CREDIT" => balance + amount,
                "DEBIT" => balance - amount,
                _ => throw new InvalidOperationException($"Unknown transaction type: {row.Type}")
            };
        }

        return balance;
    }

    public async Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var dbTransaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                "INSERT OR IGNORE INTO Accounts (Number) VALUES (@AccountNumber)",
                new { AccountNumber = transaction.AccountNumber },
                dbTransaction);

            var id = await connection.ExecuteScalarAsync<long>(
                @"INSERT INTO Transactions (AccountNumber, AmountCents, Type, CreatedAt, OccVersion)
                  VALUES (@AccountNumber, @AmountCents, @Type, @CreatedAt, @OccVersion);
                  SELECT last_insert_rowid();",
                new
                {
                    AccountNumber = transaction.AccountNumber,
                    AmountCents = (long)(transaction.Amount.Amount * 100),
                    Type = transaction.Type.ToString(),
                    CreatedAt = transaction.CreatedAt.ToString("o"),
                    OccVersion = transaction.OccVersion
                },
                dbTransaction);

            var restored = Transaction.Restore(
                id,
                transaction.AccountNumber,
                transaction.Amount,
                transaction.Type,
                transaction.CreatedAt,
                transaction.OccVersion);
            dbTransaction.Commit();
            return restored;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint violation
        {
            dbTransaction.Rollback();
            throw new ConcurrencyException($"Transaction with occ-version {transaction.OccVersion} in conflict.");
        }
    }

    private sealed record BalanceDeltaRow(long AmountCents, string Type);
}
