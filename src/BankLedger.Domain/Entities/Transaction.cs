using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Money Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public long OccVersion { get; private set; }

    public Transaction(
        Guid accountId,
        Money amount,
        TransactionType type,
        long occVersion)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(accountId));
        if (amount.Amount <= 0)
            throw new ArgumentException("Transaction amount must be positive", nameof(amount));
        if (!Enum.IsDefined(typeof(TransactionType), type))
            throw new ArgumentException("Invalid transaction type", nameof(type));
        if (occVersion <= 0)
            throw new ArgumentException("OccVersion must be positive", nameof(occVersion));

        AccountId = accountId;
        Amount = amount;
        Type = type;
        CreatedAt = DateTime.UtcNow;
        OccVersion = occVersion;
    }

    public static Transaction Restore(
        Guid id,
        Guid accountId,
        Money amount,
        TransactionType type,
        DateTime createdAt,
        long occVersion)
    {
        var transaction = new Transaction(accountId, amount, type, occVersion);
        transaction.Id = id;
        transaction.CreatedAt = createdAt;
        transaction.OccVersion = occVersion;
        return transaction;
    }
}
