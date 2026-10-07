using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Entities;

public sealed class BalanceSnapshot
{
    public Guid AccountId { get; private set; }
    public Money Balance { get; private set; }
    public Guid LastTransactionId { get; private set; }
    public long OccVersion { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public BalanceSnapshot(
        Guid accountId,
        Money balance,
        Guid lastTransactionId,
        long occVersion)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(accountId));
        if (occVersion <= 0)
            throw new ArgumentException("OccVersion must be positive", nameof(occVersion));

        AccountId = accountId;
        Balance = balance;
        LastTransactionId = lastTransactionId;
        OccVersion = occVersion;
        CreatedAt = DateTime.UtcNow;
    }

    public static BalanceSnapshot Restore(
        Guid accountId,
        Money balance,
        Guid lastTransactionId,
        long occVersion,
        DateTime createdAt)
    {
        var snapshot = new BalanceSnapshot(accountId, balance, lastTransactionId, occVersion);
        snapshot.CreatedAt = createdAt;
        return snapshot;
    }
}