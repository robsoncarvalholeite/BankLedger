using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using BankLedger.Domain.Exceptions;

namespace BankLedger.Domain.Tests;

public class TransactionTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesTransaction()
    {
        var accountId = Guid.NewGuid();
        var transaction = new Transaction(accountId, new Money(100.50m), TransactionType.CREDIT, 1);

        Assert.Equal(accountId, transaction.AccountId);
        Assert.Equal(new Money(100.50m), transaction.Amount);
        Assert.Equal(TransactionType.CREDIT, transaction.Type);
        Assert.Equal(1, transaction.OccVersion);
        Assert.True(transaction.CreatedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Constructor_EmptyGuid_Throws(string accountIdStr)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(Guid.Parse(accountIdStr), new Money(100), TransactionType.CREDIT, 1));

        Assert.Contains("Account ID is required", ex.Message);
    }

    [Fact]
    public void Constructor_ZeroAmount_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(Guid.NewGuid(), new Money(0), TransactionType.CREDIT, 1));

        Assert.Contains("Transaction amount must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_NegativeAmount_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(Guid.NewGuid(), new Money(-10), TransactionType.CREDIT, 1));

        Assert.Contains("Transaction amount must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_ZeroOccVersion_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(Guid.NewGuid(), new Money(100), TransactionType.CREDIT, 0));

        Assert.Contains("OccVersion must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_InvalidTransactionType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(Guid.NewGuid(), new Money(100), (TransactionType)999, 1));

        Assert.Contains("Invalid transaction type", ex.Message);
    }

    [Fact]
    public void Restore_CreatesTransactionWithAllProperties()
    {
        var accountId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var transaction = Transaction.Restore(
            id,
            accountId,
            new Money(100.50m),
            TransactionType.CREDIT,
            createdAt,
            1);

        Assert.Equal(id, transaction.Id);
        Assert.Equal(accountId, transaction.AccountId);
        Assert.Equal(new Money(100.50m), transaction.Amount);
        Assert.Equal(TransactionType.CREDIT, transaction.Type);
        Assert.Equal(createdAt, transaction.CreatedAt);
        Assert.Equal(1, transaction.OccVersion);
    }
}

public class AccountTests
{
    [Fact]
    public void Constructor_ValidNumber_CreatesAccount()
    {
        var id = Guid.NewGuid();
        var account = new Account(id, "123456");

        Assert.Equal(id, account.Id);
        Assert.Equal("123456", account.Number);
    }

    [Fact]
    public void Create_GeneratesNewGuid()
    {
        var account = Account.Create("123456");

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("123456", account.Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidNumber_Throws(string number)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Account(Guid.NewGuid(), number));

        Assert.Contains("Account number is required", ex.Message);
    }

    [Fact]
    public void Constructor_EmptyId_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Account(Guid.Empty, "123456"));

        Assert.Contains("Account ID is required", ex.Message);
    }
}

public class BalanceSnapshotTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesSnapshot()
    {
        var accountId = Guid.NewGuid();
        var lastTxnId = Guid.NewGuid();
        var snapshot = new BalanceSnapshot(accountId, new Money(1000.50m), lastTxnId, 5);

        Assert.Equal(accountId, snapshot.AccountId);
        Assert.Equal(new Money(1000.50m), snapshot.Balance);
        Assert.Equal(lastTxnId, snapshot.LastTransactionId);
        Assert.Equal(5, snapshot.OccVersion);
        Assert.True(snapshot.CreatedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Constructor_InvalidAccountId_Throws(string accountIdStr)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BalanceSnapshot(Guid.Parse(accountIdStr), new Money(100), Guid.NewGuid(), 0));

        Assert.Contains("Account ID is required", ex.Message);
    }

    [Fact]
    public void Constructor_ZeroOccVersion_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BalanceSnapshot(Guid.NewGuid(), new Money(100), Guid.NewGuid(), 0));

        Assert.Contains("OccVersion must be positive", ex.Message);
    }

    [Fact]
    public void Restore_CreatesSnapshotWithAllProperties()
    {
        var accountId = Guid.NewGuid();
        var lastTxnId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var snapshot = BalanceSnapshot.Restore(
            accountId,
            new Money(1000.50m),
            lastTxnId,
            5,
            createdAt);

        Assert.Equal(accountId, snapshot.AccountId);
        Assert.Equal(new Money(1000.50m), snapshot.Balance);
        Assert.Equal(lastTxnId, snapshot.LastTransactionId);
        Assert.Equal(5, snapshot.OccVersion);
        Assert.Equal(createdAt, snapshot.CreatedAt);
    }
}

public class InsufficientBalanceExceptionTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var ex = new InsufficientBalanceException(100, 50);

        Assert.Equal(100, ex.Requested);
        Assert.Equal(50, ex.Available);
        Assert.Contains("100", ex.Message);
        Assert.Contains("50", ex.Message);
    }
}

public class ConcurrencyExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new ConcurrencyException("Concurrency conflict");

        Assert.Equal("Concurrency conflict", ex.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInner_SetsBoth()
    {
        var inner = new Exception("Inner");
        var ex = new ConcurrencyException("Concurrency conflict", inner);

        Assert.Equal("Concurrency conflict", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}