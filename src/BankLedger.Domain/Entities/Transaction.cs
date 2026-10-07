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
    public Guid IdempotencyKey { get; private set; }

    public Transaction(
        string accountNumber,
        Money amount,
        TransactionType type,
        Guid idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required", nameof(accountNumber));
        if (amount.Amount <= 0)
            throw new ArgumentException("Transaction amount must be positive", nameof(amount));
        if (!Enum.IsDefined(typeof(TransactionType), type))
            throw new ArgumentException("Invalid transaction type", nameof(type));
        if (idempotencyKey == Guid.Empty)
            throw new ArgumentException("Idempotency key is required", nameof(idempotencyKey));

        AccountNumber = accountNumber;
        Amount = amount;
        Type = type;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTime.UtcNow;
    }

    internal void SetId(long id) => Id = id;
}