using BankLedger.Domain.Ports;
using BankLedger.Domain.UseCases;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.Exceptions;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Application.Tests;

public class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly List<Transaction> _transactions = [];
    private readonly Dictionary<Guid, Transaction> _byIdempotencyKey = [];
    private long _nextId = 1;

    public Task<Transaction?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        _byIdempotencyKey.TryGetValue(idempotencyKey, out var transaction);
        return Task.FromResult(transaction);
    }

    public Task<Money> GetBalanceDeltaAsync(string accountNumber, long lastTransactionId, DateTime? until, CancellationToken cancellationToken)
    {
        var relevantTransactions = _transactions
            .Where(t => t.AccountNumber == accountNumber && t.Id > lastTransactionId)
            .Where(t => until == null || t.CreatedAt <= until);

        var balance = new Money(0);
        foreach (var t in relevantTransactions)
        {
            balance = t.Type == TransactionType.CREDIT ? balance + t.Amount : balance - t.Amount;
        }
        return Task.FromResult(balance);
    }

    public Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        var restored = Transaction.Restore(
            id,
            transaction.AccountNumber,
            transaction.Amount,
            transaction.Type,
            transaction.CreatedAt,
            transaction.IdempotencyKey);
        _transactions.Add(restored);
        _byIdempotencyKey[restored.IdempotencyKey] = restored;
        return Task.FromResult(restored);
    }

    public void AddTransaction(Transaction transaction)
    {
        var id = _nextId++;
        var restored = Transaction.Restore(
            id,
            transaction.AccountNumber,
            transaction.Amount,
            transaction.Type,
            transaction.CreatedAt,
            transaction.IdempotencyKey);
        _transactions.Add(restored);
        _byIdempotencyKey[restored.IdempotencyKey] = restored;
    }
}

public class InMemorySnapshotRepository : ISnapshotRepository
{
    private readonly Dictionary<string, BalanceSnapshot> _snapshots = [];

    public Task<BalanceSnapshot?> GetAsync(string accountNumber, CancellationToken cancellationToken)
    {
        _snapshots.TryGetValue(accountNumber, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<string>> GetAccountsWithNewTransactionsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(_snapshots.Keys.ToList());
    }

    public Task<bool> UpdateAsync(BalanceSnapshot snapshot, long expectedSequence, CancellationToken cancellationToken)
    {
        if (!_snapshots.TryGetValue(snapshot.AccountNumber, out var existing))
        {
            _snapshots[snapshot.AccountNumber] = snapshot;
            return Task.FromResult(true);
        }

        if (existing.Sequence != expectedSequence)
        {
            return Task.FromResult(false);
        }

        _snapshots[snapshot.AccountNumber] = snapshot;
        return Task.FromResult(true);
    }

    public void SetSnapshot(BalanceSnapshot snapshot)
    {
        _snapshots[snapshot.AccountNumber] = snapshot;
    }
}

public class CreateTransactionUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Credit_CreatesTransactionAndReturnsIt()
    {
        var repo = new InMemoryTransactionRepository();
        var useCase = new CreateTransactionUseCase(repo);
        var idempotencyKey = Guid.NewGuid();

        var result = await useCase.ExecuteAsync("123456", new Money(100.50m), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);

        Assert.Equal("123456", result.AccountNumber);
        Assert.Equal(new Money(100.50m), result.Amount);
        Assert.Equal(TransactionType.CREDIT, result.Type);
        Assert.Equal(idempotencyKey, result.IdempotencyKey);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Debit_WithSufficientBalance_CreatesTransaction()
    {
        var repo = new InMemoryTransactionRepository();
        repo.AddTransaction(new Transaction("123456", new Money(500), TransactionType.CREDIT, Guid.NewGuid()) { });
        var useCase = new CreateTransactionUseCase(repo);
        var idempotencyKey = Guid.NewGuid();

        var result = await useCase.ExecuteAsync("123456", new Money(100), TransactionType.DEBIT, idempotencyKey, CancellationToken.None);

        Assert.Equal(TransactionType.DEBIT, result.Type);
        Assert.Equal(new Money(100), result.Amount);
    }

    [Fact]
    public async Task ExecuteAsync_Debit_InsufficientBalance_Throws()
    {
        var repo = new InMemoryTransactionRepository();
        repo.AddTransaction(new Transaction("123456", new Money(50), TransactionType.CREDIT, Guid.NewGuid()) { });
        var useCase = new CreateTransactionUseCase(repo);
        var idempotencyKey = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            useCase.ExecuteAsync("123456", new Money(100), TransactionType.DEBIT, idempotencyKey, CancellationToken.None));

        Assert.Equal("123456", ex.AccountNumber);
        Assert.Equal(100, ex.Requested);
        Assert.Equal(50, ex.Available);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateIdempotencyKey_ReturnsOriginalTransaction()
    {
        var repo = new InMemoryTransactionRepository();
        var useCase = new CreateTransactionUseCase(repo);
        var idempotencyKey = Guid.NewGuid();

        var first = await useCase.ExecuteAsync("123456", new Money(100), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);
        var second = await useCase.ExecuteAsync("123456", new Money(200), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, first.Id);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAmount_Throws()
    {
        var repo = new InMemoryTransactionRepository();
        var useCase = new CreateTransactionUseCase(repo);
        var idempotencyKey = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync("123456", new Money(0), TransactionType.CREDIT, idempotencyKey, CancellationToken.None));
    }
}

public class GetBalanceUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_NoSnapshotNoTransactions_ReturnsZero()
    {
        var txRepo = new InMemoryTransactionRepository();
        var snapRepo = new InMemorySnapshotRepository();
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync("123456", null, CancellationToken.None);

        Assert.Equal(new Money(0), result);
    }

    [Fact]
    public async Task ExecuteAsync_WithTransactions_CalculatesBalance()
    {
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.NewGuid()) { });
        txRepo.AddTransaction(new Transaction("123456", new Money(50), TransactionType.DEBIT, Guid.NewGuid()) { });
        var snapRepo = new InMemorySnapshotRepository();
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync("123456", null, CancellationToken.None);

        Assert.Equal(new Money(50), result);
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshot_UsesSnapshotAsBase()
    {
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.NewGuid()) { });
        txRepo.AddTransaction(new Transaction("123456", new Money(50), TransactionType.DEBIT, Guid.NewGuid()) { });
        var snapRepo = new InMemorySnapshotRepository();
        snapRepo.SetSnapshot(new BalanceSnapshot("123456", new Money(500), 0, 5));
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync("123456", null, CancellationToken.None);

        Assert.Equal(new Money(550), result);
    }

    [Fact]
    public async Task ExecuteAsync_WithFromDate_FiltersTransactions()
    {
        var txRepo = new InMemoryTransactionRepository();
        var baseTime = DateTime.UtcNow;
        var t1 = new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.NewGuid()) { };
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t1, baseTime);
        var t2 = new Transaction("123456", new Money(50), TransactionType.CREDIT, Guid.NewGuid()) { };
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t2, baseTime.AddSeconds(2));
        txRepo.AddTransaction(t1);
        txRepo.AddTransaction(t2);
        var snapRepo = new InMemorySnapshotRepository();
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var until = baseTime.AddSeconds(1);
        var result = await useCase.ExecuteAsync("123456", until, CancellationToken.None);

        Assert.Equal(new Money(100), result);
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotBoundary_DoesNotDoubleCount()
    {
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.NewGuid()) { });
        txRepo.AddTransaction(new Transaction("123456", new Money(50), TransactionType.CREDIT, Guid.NewGuid()) { });
        var snapRepo = new InMemorySnapshotRepository();
        snapRepo.SetSnapshot(new BalanceSnapshot("123456", new Money(100), 1, 5));
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync("123456", null, CancellationToken.None);

        Assert.Equal(new Money(150), result);
    }
}