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
        var idempotencyKey = Guid.NewGuid();
        var transaction = new Transaction("123456", new Money(100.50m), TransactionType.CREDIT, idempotencyKey);

        Assert.Equal("123456", transaction.AccountNumber);
        Assert.Equal(new Money(100.50m), transaction.Amount);
        Assert.Equal(TransactionType.CREDIT, transaction.Type);
        Assert.Equal(idempotencyKey, transaction.IdempotencyKey);
        Assert.True(transaction.CreatedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidAccountNumber_Throws(string accountNumber)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction(accountNumber, new Money(100), TransactionType.CREDIT, Guid.NewGuid()));

        Assert.Contains("Account number is required", ex.Message);
    }

    [Fact]
    public void Constructor_ZeroAmount_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction("123456", new Money(0), TransactionType.CREDIT, Guid.NewGuid()));

        Assert.Contains("Transaction amount must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_NegativeAmount_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction("123456", new Money(-10), TransactionType.CREDIT, Guid.NewGuid()));

        Assert.Contains("Transaction amount must be positive", ex.Message);
    }

    [Fact]
    public void Constructor_EmptyIdempotencyKey_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.Empty));

        Assert.Contains("Idempotency key is required", ex.Message);
    }
}

public class AccountTests
{
    [Fact]
    public void Constructor_ValidNumber_CreatesAccount()
    {
        var account = new Account("123456");

        Assert.Equal("123456", account.Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidNumber_Throws(string number)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Account(number));

        Assert.Contains("Account number is required", ex.Message);
    }
}

public class BalanceSnapshotTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesSnapshot()
    {
        var snapshot = new BalanceSnapshot("123456", new Money(1000.50m), 10, 5);

        Assert.Equal("123456", snapshot.AccountNumber);
        Assert.Equal(new Money(1000.50m), snapshot.Balance);
        Assert.Equal(10, snapshot.LastTransactionId);
        Assert.Equal(5, snapshot.Sequence);
        Assert.True(snapshot.CreatedAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidAccountNumber_Throws(string accountNumber)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BalanceSnapshot(accountNumber, new Money(100), 0, 0));

        Assert.Contains("Account number is required", ex.Message);
    }

    [Fact]
    public void Constructor_NegativeLastTransactionId_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BalanceSnapshot("123456", new Money(100), -1, 0));

        Assert.Contains("Last transaction ID must be non-negative", ex.Message);
    }

    [Fact]
    public void Constructor_NegativeSequence_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new BalanceSnapshot("123456", new Money(100), 0, -1));

        Assert.Contains("Sequence must be non-negative", ex.Message);
    }

    [Fact]
    public void Update_ReturnsNewSnapshotWithIncrementedSequence()
    {
        var original = new BalanceSnapshot("123456", new Money(100), 10, 5);
        var updated = original.Update(new Money(200), 15);

        Assert.Equal("123456", updated.AccountNumber);
        Assert.Equal(new Money(200), updated.Balance);
        Assert.Equal(15, updated.LastTransactionId);
        Assert.Equal(6, updated.Sequence);
        Assert.NotSame(original, updated);
    }
}

public class InsufficientBalanceExceptionTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var ex = new InsufficientBalanceException("123456", 100, 50);

        Assert.Equal("123456", ex.AccountNumber);
        Assert.Equal(100, ex.Requested);
        Assert.Equal(50, ex.Available);
        Assert.Contains("123456", ex.Message);
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