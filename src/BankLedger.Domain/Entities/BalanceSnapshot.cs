using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Entities;

public sealed class BalanceSnapshot
{
    public string AccountNumber { get; private set; }
    public Money Balance { get; private set; }
    public long LastTransactionId { get; private set; }
    public long Sequence { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public BalanceSnapshot(
        string accountNumber,
        Money balance,
        long lastTransactionId,
        long sequence)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required", nameof(accountNumber));
        if (lastTransactionId < 0)
            throw new ArgumentException("Last transaction ID must be non-negative", nameof(lastTransactionId));
        if (sequence < 0)
            throw new ArgumentException("Sequence must be non-negative", nameof(sequence));

        AccountNumber = accountNumber;
        Balance = balance;
        LastTransactionId = lastTransactionId;
        Sequence = sequence;
        CreatedAt = DateTime.UtcNow;
    }

    public BalanceSnapshot Update(Money newBalance, long newLastTransactionId)
    {
        return new BalanceSnapshot(AccountNumber, newBalance, newLastTransactionId, Sequence + 1);
    }
}