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
    private static readonly Dictionary<TransactionType, char> _typeMap = new()
    {
        [TransactionType.CREDIT] = 'C',
        [TransactionType.DEBIT] = 'D'
    };

    public TransactionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<(Money balance, Guid lastTransactionId)> GetBalanceDeltaAsync(Guid accountId, Guid transactionId, DateTime? until, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            SELECT
                t.id as Id,
                t.amount_in_cents as AmountCents,
                t.type as Type
            FROM transactions t
            WHERE t.account_id = @AccountId
              AND t.id > @TransactionId
            """;

        object parameters = new
        {
            AccountId = accountId.ToString(),
            TransactionId = transactionId.ToString()
        };

        if (until.HasValue)
        {
            sql += " AND t.created_at <= @Until";
            parameters = new
            {
                AccountId = accountId.ToString(),
                TransactionId = transactionId.ToString(),
                Until = until.Value.ToString("o")
            };
        }

        var rows = await connection.QueryAsync<BalanceDeltaRow>(sql, parameters);

        var balance = new Money(0);
        foreach (var row in rows)
        {
            var amount = new Money(row.AmountCents / 100m);

            var type = _typeMap.ToDictionary(x => x.Value, x => x.Key)[row.Type[0]];
            balance = type switch
            {
                TransactionType.CREDIT => balance + amount,
                TransactionType.DEBIT => balance - amount,
                _ => throw new InvalidOperationException($"Unknown transaction type: {row.Type}")
            };
        }
        var lastTransactionId = Guid.Parse(rows.MaxBy(r => r.Id)?.Id ?? Guid.Empty.ToString());

        return (balance, lastTransactionId);
    }

    public async Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var dbTransaction = connection.BeginTransaction();

        try
        {
            var accountIdStr = transaction.AccountId.ToString();

            var id = transaction.Id == Guid.Empty ? Guid.CreateVersion7() : transaction.Id;
            var idStr = id.ToString();

            var typeChar = _typeMap[transaction.Type];

            await connection.ExecuteAsync(
                @"INSERT INTO transactions (id, account_id, amount_in_cents, type, created_at, occ_version)
                  VALUES (@Id, @AccountId, @AmountInCents, @Type, @CreatedAt, @OccVersion)",
                new
                {
                    Id = idStr,
                    AccountId = accountIdStr,
                    AmountInCents = (long)(transaction.Amount.Amount * 100),
                    Type = typeChar.ToString(),
                    CreatedAt = transaction.CreatedAt.ToString("o"),
                    OccVersion = transaction.OccVersion
                },
                dbTransaction);

            dbTransaction.Commit();

            return Transaction.Restore(
                id,
                transaction.AccountId,
                transaction.Amount,
                transaction.Type,
                transaction.CreatedAt,
                transaction.OccVersion);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint violation
        {
            dbTransaction.Rollback();
            throw new ConcurrencyException($"Transaction with occ-version {transaction.OccVersion} in conflict for account {transaction.AccountId}.");
        }
    }

    private sealed record BalanceDeltaRow(string Id, long AmountCents, string Type);
}
