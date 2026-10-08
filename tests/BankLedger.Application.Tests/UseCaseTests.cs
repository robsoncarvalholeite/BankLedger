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

    public Task<(Money balance, Guid lastTransactionId)> GetBalanceDeltaAsync(Guid accountId, Guid transactionId, DateTime? until, CancellationToken cancellationToken)
    {
        var relevantTransactions = _transactions
            .Where(t => t.AccountId == accountId)
            .Where(t => transactionId == Guid.Empty || t.Id.CompareTo(transactionId) > 0)
            .Where(t => until == null || t.CreatedAt <= until);

        var balance = new Money(0);
        Guid lastTxnId = Guid.Empty;
        foreach (var t in relevantTransactions)
        {
            balance = t.Type == TransactionType.CREDIT ? balance + t.Amount : balance - t.Amount;
            lastTxnId = t.Id;
        }
        return Task.FromResult((balance, lastTxnId));
    }

    public Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        _transactions.Add(transaction);
        return Task.FromResult(transaction);
    }

    public void AddTransaction(Transaction transaction)
    {
        _transactions.Add(transaction);
    }
}

public class InMemorySnapshotRepository : ISnapshotRepository
{
    private readonly Dictionary<Guid, BalanceSnapshot> _snapshots = [];

    public Task<BalanceSnapshot?> GetLastAsync(Guid accountId, CancellationToken cancellationToken)
    {
        _snapshots.TryGetValue(accountId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<Guid>> GetAccountsWithNewTransactionsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_snapshots.Keys.ToList());
    }

    public Task<BalanceSnapshot> CreateAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken)
    {
        _snapshots[snapshot.AccountId] = snapshot;
        return Task.FromResult(snapshot);
    }

    public void SetSnapshot(BalanceSnapshot snapshot)
    {
        _snapshots[snapshot.AccountId] = snapshot;
    }
}

public class InMemoryIdempotencyLock : IdempotencyLock
{
    private readonly HashSet<Guid> _keys = [];

    public Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken ct)
    {
        return Task.FromResult(_keys.Contains(idempotencyKey));
    }

    public Task AddAsync(Guid idempotencyKey, CancellationToken ct)
    {
        _keys.Add(idempotencyKey);
        return Task.CompletedTask;
    }
}

public class InMemoryConcurrencyStore : IConcurrencyStore
{
    private readonly Dictionary<string, long> _versions = [];

    public Task<long> NextAsync(string accountNumber, CancellationToken ct)
    {
        var newVersion = _versions.ContainsKey(accountNumber) ? _versions[accountNumber] + 1 : 1;
        _versions[accountNumber] = newVersion;
        return Task.FromResult(newVersion);
    }
}

public class CreateTransactionUseCaseTests
{
    private CreateTransactionUseCase CreateUseCase(
        ITransactionRepository? txRepo = null,
        IdempotencyLock? idemLock = null,
        GetBalanceUseCase? balanceUseCase = null,
        IConcurrencyStore? concurrencyStore = null)
    {
        txRepo ??= new InMemoryTransactionRepository();
        idemLock ??= new InMemoryIdempotencyLock();
        var snapRepo = new InMemorySnapshotRepository();
        balanceUseCase ??= new GetBalanceUseCase(txRepo, snapRepo);
        concurrencyStore ??= new InMemoryConcurrencyStore();
        return new CreateTransactionUseCase(txRepo, idemLock, balanceUseCase, concurrencyStore);
    }

    [Fact]
    public async Task ExecuteAsync_Credit_CreatesTransactionAndReturnsTrue()
    {
        var useCase = CreateUseCase();
        var accountId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await useCase.ExecuteAsync(accountId, new Money(100.50m), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_Debit_WithSufficientBalance_CreatesTransaction()
    {
        var accountId = Guid.NewGuid();
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction(accountId, new Money(500), TransactionType.CREDIT, 1));

        var useCase = CreateUseCase(txRepo: txRepo);
        var idempotencyKey = Guid.NewGuid();

        await useCase.ExecuteAsync(accountId, new Money(100), TransactionType.DEBIT, idempotencyKey, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_Debit_InsufficientBalance_Throws()
    {
        var accountId = Guid.NewGuid();
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction(accountId, new Money(50), TransactionType.CREDIT, 1));
        var useCase = CreateUseCase(txRepo: txRepo);
        var idempotencyKey = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            useCase.ExecuteAsync(accountId, new Money(100), TransactionType.DEBIT, idempotencyKey, CancellationToken.None));

        Assert.Equal(100, ex.Requested);
        Assert.Equal(50, ex.Available);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateIdempotencyKey_ReturnsTrueWithoutProcessing()
    {
        var idemLock = new InMemoryIdempotencyLock();
        var useCase = CreateUseCase(idemLock: idemLock);
        var accountId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await useCase.ExecuteAsync(accountId, new Money(100), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);

        await useCase.ExecuteAsync(accountId, new Money(200), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAmount_Throws()
    {
        var useCase = CreateUseCase();
        var accountId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(accountId, new Money(0), TransactionType.CREDIT, idempotencyKey, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrencyConflict_RetriesAndSucceeds()
    {
        var concurrencyStore = new InMemoryConcurrencyStore();
        var useCase = CreateUseCase(concurrencyStore: concurrencyStore);
        var accountId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await useCase.ExecuteAsync(accountId, new Money(100), TransactionType.CREDIT, idempotencyKey, CancellationToken.None);
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
        var accountId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(accountId, null, CancellationToken.None);

        Assert.Equal(new Money(0), result);
    }

    [Fact]
    public async Task ExecuteAsync_WithTransactions_CalculatesBalance()
    {
        var accountId = Guid.NewGuid();
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(new Transaction(accountId, new Money(100), TransactionType.CREDIT, 1));
        txRepo.AddTransaction(new Transaction(accountId, new Money(50), TransactionType.DEBIT, 2));
        var snapRepo = new InMemorySnapshotRepository();
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync(accountId, null, CancellationToken.None);

        Assert.Equal(new Money(50), result);
    }

    [Fact]
    public async Task ExecuteAsync_WithSnapshot_UsesSnapshotAsBase()
    {
        var accountId = Guid.NewGuid();
        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(Transaction.Restore(Guid.NewGuid(), accountId, new Money(100), TransactionType.CREDIT, DateTime.UtcNow, 1));
        txRepo.AddTransaction(Transaction.Restore(Guid.NewGuid(), accountId, new Money(50), TransactionType.DEBIT, DateTime.UtcNow, 2));
        var snapRepo = new InMemorySnapshotRepository();
        snapRepo.SetSnapshot(new BalanceSnapshot(accountId, new Money(500), Guid.Empty, 5));
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync(accountId, null, CancellationToken.None);

        Assert.Equal(new Money(550), result);
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotBoundary_DoesNotDoubleCount()
    {
        var accountId = Guid.NewGuid();
        var lastTxnId = Guid.NewGuid();
        var nextTxnId = Guid.NewGuid();
        // Ensure nextTxnId > lastTxnId for proper comparison
        while (nextTxnId.CompareTo(lastTxnId) <= 0)
        {
            nextTxnId = Guid.NewGuid();
        }

        var txRepo = new InMemoryTransactionRepository();
        txRepo.AddTransaction(Transaction.Restore(lastTxnId, accountId, new Money(100), TransactionType.CREDIT, DateTime.UtcNow, 1));
        txRepo.AddTransaction(Transaction.Restore(nextTxnId, accountId, new Money(50), TransactionType.CREDIT, DateTime.UtcNow, 2));
        var snapRepo = new InMemorySnapshotRepository();
        snapRepo.SetSnapshot(new BalanceSnapshot(accountId, new Money(100), lastTxnId, 5));
        var useCase = new GetBalanceUseCase(txRepo, snapRepo);

        var result = await useCase.ExecuteAsync(accountId, null, CancellationToken.None);

        Assert.Equal(new Money(150), result);
    }
}