using Dapper;
using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace BankLedger.Infrastructure.Persistence;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public TransactionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Transaction?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<TransactionRow>(
            "SELECT Id, AccountNumber, AmountCents, Type, CreatedAt, IdempotencyKey FROM Transactions WHERE IdempotencyKey = @IdempotencyKey",
            new { IdempotencyKey = idempotencyKey.ToString() });

        return row?.ToDomain();
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
            balance = row.Type == "CREDIT" ? balance + amount : balance - amount;
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
                @"INSERT INTO Transactions (AccountNumber, AmountCents, Type, CreatedAt, IdempotencyKey)
                  VALUES (@AccountNumber, @AmountCents, @Type, @CreatedAt, @IdempotencyKey);
                  SELECT last_insert_rowid();",
                new
                {
                    AccountNumber = transaction.AccountNumber,
                    AmountCents = (long)(transaction.Amount.Amount * 100),
                    Type = transaction.Type.ToString(),
                    CreatedAt = transaction.CreatedAt.ToString("o"),
                    IdempotencyKey = transaction.IdempotencyKey.ToString()
                },
                dbTransaction);

            var restored = Transaction.Restore(
                id,
                transaction.AccountNumber,
                transaction.Amount,
                transaction.Type,
                transaction.CreatedAt,
                transaction.IdempotencyKey);
            dbTransaction.Commit();
            return restored;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint violation
        {
            dbTransaction.Rollback();
            var existing = await GetByIdempotencyKeyAsync(transaction.IdempotencyKey, cancellationToken);
            return existing!;
        }
    }

    private sealed record TransactionRow(long Id, string AccountNumber, long AmountCents, string Type, string CreatedAt, string IdempotencyKey)
    {
        public Transaction ToDomain()
        {
            return Transaction.Restore(
                Id,
                AccountNumber,
                new Money(AmountCents / 100m),
                Enum.Parse<TransactionType>(Type),
                DateTime.Parse(CreatedAt),
                Guid.Parse(IdempotencyKey));
        }
    }

    private sealed record BalanceDeltaRow(long AmountCents, string Type);
}