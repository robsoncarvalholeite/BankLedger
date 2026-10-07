using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Entities;

public sealed class Transaction
{
    public long Id { get; private set; }
    public string AccountNumber { get; private set; }
    public Money Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public long OccVersion { get; private set; }

    public Transaction(
        string accountNumber,
        Money amount,
        TransactionType type,
        long occVersion)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required", nameof(accountNumber));
        if (amount.Amount <= 0)
            throw new ArgumentException("Transaction amount must be positive", nameof(amount));
        if (!Enum.IsDefined(typeof(TransactionType), type))
            throw new ArgumentException("Invalid transaction type", nameof(type));

        AccountNumber = accountNumber;
        Amount = amount;
        Type = type;
        CreatedAt = DateTime.UtcNow;
        OccVersion = occVersion;
    }

    public static Transaction Restore(
        long id,
        string accountNumber,
        Money amount,
        TransactionType type,
        DateTime createdAt,
        long occVersion)
    {
        var transaction = new Transaction(accountNumber, amount, type, occVersion);
        transaction.Id = id;
        transaction.CreatedAt = createdAt;
        transaction.OccVersion = occVersion;
        return transaction;
    }
}
