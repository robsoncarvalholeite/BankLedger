using BankLedger.Application.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using BankLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Dapper;

namespace BankLedger.Infrastructure.Tests;

public class SqliteRepositoryTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TransactionRepository _transactionRepository;
    private readonly SnapshotRepository _snapshotRepository;

    public SqliteRepositoryTests()
    {
        _databasePath = Path.GetTempFileName();
        var connectionString = $"Data Source={_databasePath}";
        
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        DatabaseInitializer.Initialize(connection);
        
        _factory = new SqliteConnectionFactory(connectionString);
        _transactionRepository = new TransactionRepository(_factory);
        _snapshotRepository = new SnapshotRepository(_factory);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private SqliteConnection CreateFreshConnection()
    {
        var connectionString = $"Data Source={_databasePath}";
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    [Fact]
    public async Task Schema_Creation_CreatesAllTables()
    {
        using var connection = CreateFreshConnection();
        
        var tables = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
        
        var tableNames = tables.ToHashSet();
        Assert.Contains("Accounts", tableNames);
        Assert.Contains("Transactions", tableNames);
        Assert.Contains("BalanceSnapshots", tableNames);
    }

    [Fact]
    public async Task Schema_Creation_CreatesIndexes()
    {
        using var connection = CreateFreshConnection();
        
        var indexes = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'");
        
        var indexNames = indexes.ToHashSet();
        Assert.Contains("IX_Transactions_Account_Id", indexNames);
        Assert.Contains("IX_Transactions_Account_CreatedAt", indexNames);
        Assert.Contains("IX_Transactions_Idempotency", indexNames);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_PersistsTransaction()
    {
        var transaction = new Transaction("123456", new Money(100.50m), TransactionType.CREDIT, Guid.NewGuid());
        
        var result = await _transactionRepository.CreateAsync(transaction, CancellationToken.None);
        
        Assert.Equal(1, result.Id);
        Assert.Equal("123456", result.AccountNumber);
        Assert.Equal(new Money(100.50m), result.Amount);
        Assert.Equal(TransactionType.CREDIT, result.Type);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_InsertsAccountIfNotExists()
    {
        var transaction = new Transaction("999999", new Money(50), TransactionType.CREDIT, Guid.NewGuid());
        
        await _transactionRepository.CreateAsync(transaction, CancellationToken.None);
        
        using var connection = CreateFreshConnection();
        var account = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT Number FROM Accounts WHERE Number = @Number", new { Number = "999999" });
        
        Assert.Equal("999999", account);
    }

    [Fact]
    public async Task TransactionRepository_GetByIdempotencyKeyAsync_ReturnsTransaction()
    {
        var idempotencyKey = Guid.NewGuid();
        var transaction = new Transaction("123456", new Money(100), TransactionType.CREDIT, idempotencyKey);
        var created = await _transactionRepository.CreateAsync(transaction, CancellationToken.None);
        
        var result = await _transactionRepository.GetByIdempotencyKeyAsync(idempotencyKey, CancellationToken.None);
        
        Assert.NotNull(result);
        Assert.Equal(idempotencyKey, result!.IdempotencyKey);
        Assert.Equal("123456", result.AccountNumber);
    }

    [Fact]
    public async Task TransactionRepository_GetByIdempotencyKeyAsync_ReturnsNullForMissing()
    {
        var result = await _transactionRepository.GetByIdempotencyKeyAsync(Guid.NewGuid(), CancellationToken.None);
        
        Assert.Null(result);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_Idempotency_ReturnsExistingTransaction()
    {
        var idempotencyKey = Guid.NewGuid();
        var transaction1 = new Transaction("123456", new Money(100), TransactionType.CREDIT, idempotencyKey);
        await _transactionRepository.CreateAsync(transaction1, CancellationToken.None);
        
        var transaction2 = new Transaction("123456", new Money(200), TransactionType.CREDIT, idempotencyKey);
        var result = await _transactionRepository.CreateAsync(transaction2, CancellationToken.None);
        
        Assert.Equal(1, result.Id);
        Assert.Equal(new Money(100), result.Amount);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_CalculatesCorrectly()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(500), TransactionType.CREDIT, id1), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(100), TransactionType.DEBIT, id2), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(50), TransactionType.CREDIT, id3), CancellationToken.None);
        
        var delta = await _transactionRepository.GetBalanceDeltaAsync("123456", 0, null, CancellationToken.None);
        
        Assert.Equal(new Money(450), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_WithLastTransactionId_FiltersCorrectly()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        
        var t1 = await _transactionRepository.CreateAsync(new Transaction("123456", new Money(500), TransactionType.CREDIT, id1), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(100), TransactionType.DEBIT, id2), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(50), TransactionType.CREDIT, id3), CancellationToken.None);
        
        var delta = await _transactionRepository.GetBalanceDeltaAsync("123456", t1.Id, null, CancellationToken.None);
        
        Assert.Equal(new Money(-50), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_WithUntil_FiltersByDate()
    {
        var baseTime = DateTime.UtcNow;
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        
        var t1 = new Transaction("123456", new Money(100), TransactionType.CREDIT, id1);
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t1, baseTime);
        await _transactionRepository.CreateAsync(t1, CancellationToken.None);
        
        var t2 = new Transaction("123456", new Money(50), TransactionType.CREDIT, id2);
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t2, baseTime.AddSeconds(10));
        await _transactionRepository.CreateAsync(t2, CancellationToken.None);
        
        var until = baseTime.AddSeconds(5);
        var delta = await _transactionRepository.GetBalanceDeltaAsync("123456", 0, until, CancellationToken.None);
        
        Assert.Equal(new Money(100), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_EmptyAccount_ReturnsZero()
    {
        var delta = await _transactionRepository.GetBalanceDeltaAsync("999999", 0, null, CancellationToken.None);
        
        Assert.Equal(new Money(0), delta);
    }

    [Fact]
    public async Task SnapshotRepository_GetAsync_ReturnsNullForMissing()
    {
        var result = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        
        Assert.Null(result);
    }

    [Fact]
    public async Task SnapshotRepository_UpdateAsync_CreatesNewSnapshot()
    {
        var id = Guid.NewGuid();
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(100), TransactionType.CREDIT, id), CancellationToken.None);
        
        // Ensure Account exists in the same connection context
        using (var conn = CreateFreshConnection())
        {
            await conn.ExecuteAsync("INSERT OR IGNORE INTO Accounts (Number) VALUES (@Number)", new { Number = "123456" });
        }
        
        var snapshot = new BalanceSnapshot("123456", new Money(1000), 10, 0);
        
        var success = await _snapshotRepository.UpdateAsync(snapshot, 0, CancellationToken.None);
        
        Assert.True(success);
        
        var retrieved = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal("123456", retrieved!.AccountNumber);
        Assert.Equal(new Money(1000), retrieved.Balance);
        Assert.Equal(10, retrieved.LastTransactionId);
        Assert.Equal(0, retrieved.Sequence);
    }

    [Fact]
    public async Task SnapshotRepository_UpdateAsync_UpdatesExistingSnapshot()
    {
        var id = Guid.NewGuid();
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(100), TransactionType.CREDIT, id), CancellationToken.None);
        
        using (var conn = CreateFreshConnection())
        {
            await conn.ExecuteAsync("INSERT OR IGNORE INTO Accounts (Number) VALUES (@Number)", new { Number = "123456" });
        }
        
        var snapshot1 = new BalanceSnapshot("123456", new Money(1000), 10, 0);
        await _snapshotRepository.UpdateAsync(snapshot1, 0, CancellationToken.None);
        
        var retrieved1 = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        var snapshot2 = retrieved1!.Update(new Money(1500), 15);
        var success = await _snapshotRepository.UpdateAsync(snapshot2, retrieved1.Sequence, CancellationToken.None);
        
        Assert.True(success);
        
        var retrieved2 = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        Assert.Equal(new Money(1500), retrieved2!.Balance);
        Assert.Equal(15, retrieved2.LastTransactionId);
        Assert.Equal(retrieved1.Sequence + 1, retrieved2.Sequence);
    }

    [Fact]
    public async Task SnapshotRepository_UpdateAsync_OCC_FailsOnConcurrentUpdate()
    {
        var id = Guid.NewGuid();
        await _transactionRepository.CreateAsync(new Transaction("123456", new Money(100), TransactionType.CREDIT, id), CancellationToken.None);
        
        using (var conn = CreateFreshConnection())
        {
            await conn.ExecuteAsync("INSERT OR IGNORE INTO Accounts (Number) VALUES (@Number)", new { Number = "123456" });
        }
        
        var snapshot1 = new BalanceSnapshot("123456", new Money(1000), 10, 0);
        await _snapshotRepository.UpdateAsync(snapshot1, 0, CancellationToken.None);
        
        var retrieved1 = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        var baseSequence = retrieved1!.Sequence; // 0
        
        // First concurrent request: uses baseSequence (0) -> succeeds, Sequence becomes 1
        var firstUpdate = retrieved1.Update(new Money(1500), 15);
        var success1 = await _snapshotRepository.UpdateAsync(firstUpdate, baseSequence, CancellationToken.None);
        
        // Second concurrent request: uses same baseSequence (0) -> fails, Sequence is now 1
        var secondUpdate = retrieved1.Update(new Money(1500), 15);
        var success2 = await _snapshotRepository.UpdateAsync(secondUpdate, baseSequence, CancellationToken.None);
        
        Assert.True(success1);
        Assert.False(success2);
        
        // Verify final state
        var final = await _snapshotRepository.GetAsync("123456", CancellationToken.None);
        Assert.Equal(1, final!.Sequence);
        Assert.Equal(new Money(1500), final.Balance);
    }

    [Fact]
    public async Task SnapshotRepository_GetAccountsWithNewTransactionsAsync_ReturnsAccountsWithNewTransactions()
    {
        var id1 = Guid.NewGuid();
        var t1 = await _transactionRepository.CreateAsync(new Transaction("111111", new Money(100), TransactionType.CREDIT, id1), CancellationToken.None);
        
        var snapshot = new BalanceSnapshot("111111", new Money(100), t1.Id, 0);
        await _snapshotRepository.UpdateAsync(snapshot, 0, CancellationToken.None);
        
        var id2 = Guid.NewGuid();
        await _transactionRepository.CreateAsync(new Transaction("111111", new Money(50), TransactionType.CREDIT, id2), CancellationToken.None);
        
        var accounts = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(CancellationToken.None);
        
        Assert.Contains("111111", accounts);
    }

    [Fact]
    public async Task SnapshotRepository_GetAccountsWithNewTransactionsAsync_ExcludesAccountsWithoutNewTransactions()
    {
        var id1 = Guid.NewGuid();
        var t1 = await _transactionRepository.CreateAsync(new Transaction("111111", new Money(100), TransactionType.CREDIT, id1), CancellationToken.None);
        
        var snapshot = new BalanceSnapshot("111111", new Money(100), t1.Id, 0);
        await _snapshotRepository.UpdateAsync(snapshot, 0, CancellationToken.None);
        
        var accounts = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(CancellationToken.None);
        
        Assert.DoesNotContain("111111", accounts);
    }

    [Fact]
    public async Task Money_CentsConversion_WorksCorrectly()
    {
        var transaction = new Transaction("123456", new Money(100.50m), TransactionType.CREDIT, Guid.NewGuid());
        var created = await _transactionRepository.CreateAsync(transaction, CancellationToken.None);
        
        using var connection = CreateFreshConnection();
        var row = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT AmountCents FROM Transactions WHERE Id = @Id", new { Id = created.Id });
        
        Assert.NotNull(row);
        Assert.Equal(10050L, (long)row!.AmountCents);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_StoresCorrectType()
    {
        var creditTx = new Transaction("123456", new Money(100), TransactionType.CREDIT, Guid.NewGuid());
        var debitTx = new Transaction("123456", new Money(50), TransactionType.DEBIT, Guid.NewGuid());
        
        await _transactionRepository.CreateAsync(creditTx, CancellationToken.None);
        await _transactionRepository.CreateAsync(debitTx, CancellationToken.None);
        
        using var connection = CreateFreshConnection();
        var types = await connection.QueryAsync<string>(
            "SELECT Type FROM Transactions WHERE AccountNumber = @AccountNumber ORDER BY Id", 
            new { AccountNumber = "123456" });
        
        Assert.Equal(new[] { "CREDIT", "DEBIT" }, types);
    }
}