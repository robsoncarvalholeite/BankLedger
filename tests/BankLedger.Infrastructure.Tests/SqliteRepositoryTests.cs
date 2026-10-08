using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.Enums;
using BankLedger.Domain.ValueObjects;
using BankLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Dapper;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace BankLedger.Infrastructure.Tests;

[Collection("DatabaseCollection")]
public class SqliteRepositoryTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TransactionRepository _transactionRepository;
    private readonly SnapshotRepository _snapshotRepository;

    public SqliteRepositoryTests(ITestOutputHelper output)
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"bank_test_{Guid.NewGuid()}.db");
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

    private async Task<Guid> CreateAccountAsync(string number = "123456")
    {
        var accountId = Guid.NewGuid();
        using var conn = CreateFreshConnection();
        await conn.ExecuteAsync("INSERT INTO accounts (id, number) VALUES (@Id, @Number)",
            new { Id = accountId.ToString(), Number = number });
        return accountId;
    }

    [Fact]
    public async Task Schema_Creation_CreatesAllTables()
    {
        using var connection = CreateFreshConnection();

        var tables = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");

        var tableNames = tables.ToHashSet();
        Assert.Contains("accounts", tableNames);
        Assert.Contains("transactions", tableNames);
        Assert.Contains("balance_snapshots", tableNames);
    }

    [Fact]
    public async Task Schema_Creation_CreatesIndexes()
    {
        using var connection = CreateFreshConnection();

        var indexes = await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'");

        var indexNames = indexes.ToHashSet();
        Assert.Contains("ix_transactions_account_id", indexNames);
        Assert.Contains("ix_transactions_account_created_at", indexNames);
        Assert.Contains("uk_transactions_account_occ_version", indexNames);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_PersistsTransaction()
    {
        var accountId = await CreateAccountAsync();

        var transaction = new Transaction(accountId, new Money(100.50m), TransactionType.CREDIT, 1);

        var result = await _transactionRepository.CreateAsync(transaction, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(accountId, result.AccountId);
        Assert.Equal(new Money(100.50m), result.Amount);
        Assert.Equal(TransactionType.CREDIT, result.Type);
        Assert.Equal(1, result.OccVersion);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_InsertsAccountIfNotExists()
    {
        var accountId = await CreateAccountAsync();

        var transaction = new Transaction(accountId, new Money(50), TransactionType.CREDIT, 1);

        await _transactionRepository.CreateAsync(transaction, CancellationToken.None);

        using var connection = CreateFreshConnection();
        var account = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT number FROM accounts WHERE id = @Id", new { Id = accountId.ToString() });

        Assert.NotNull(account);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_CalculatesCorrectly()
    {
        var accountId = await CreateAccountAsync();

        await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(500), TransactionType.CREDIT, 1), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(100), TransactionType.DEBIT, 2), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(50), TransactionType.CREDIT, 3), CancellationToken.None);

        var (delta, _) = await _transactionRepository.GetBalanceDeltaAsync(accountId, Guid.Empty, null, CancellationToken.None);

        Assert.Equal(new Money(450), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_WithLastTransactionId_FiltersCorrectly()
    {
        var accountId = await CreateAccountAsync();

        var t1 = await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(500), TransactionType.CREDIT, 1), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(100), TransactionType.DEBIT, 2), CancellationToken.None);
        await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(50), TransactionType.CREDIT, 3), CancellationToken.None);

        var (delta, _) = await _transactionRepository.GetBalanceDeltaAsync(accountId, t1.Id, null, CancellationToken.None);

        Assert.Equal(new Money(-50), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_WithUntil_FiltersByDate()
    {
        var accountId = await CreateAccountAsync();

        var baseTime = DateTime.UtcNow;

        var t1 = new Transaction(accountId, new Money(100), TransactionType.CREDIT, 1);
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t1, baseTime);
        await _transactionRepository.CreateAsync(t1, CancellationToken.None);

        var t2 = new Transaction(accountId, new Money(50), TransactionType.CREDIT, 2);
        typeof(Transaction).GetProperty("CreatedAt")!.SetValue(t2, baseTime.AddSeconds(10));
        await _transactionRepository.CreateAsync(t2, CancellationToken.None);

        var until = baseTime.AddSeconds(5);
        var (delta, _) = await _transactionRepository.GetBalanceDeltaAsync(accountId, Guid.Empty, until, CancellationToken.None);

        Assert.Equal(new Money(100), delta);
    }

    [Fact]
    public async Task TransactionRepository_GetBalanceDeltaAsync_EmptyAccount_ReturnsZero()
    {
        var accountId = await CreateAccountAsync();
        var (delta, _) = await _transactionRepository.GetBalanceDeltaAsync(accountId, Guid.Empty, null, CancellationToken.None);

        Assert.Equal(new Money(0), delta);
    }

    [Fact]
    public async Task SnapshotRepository_GetLastAsync_ReturnsNullForMissing()
    {
        var accountId = await CreateAccountAsync();
        var result = await _snapshotRepository.GetLastAsync(accountId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SnapshotRepository_CreateAsync_CreatesNewSnapshot()
    {
        var accountId = await CreateAccountAsync();
        var lastTxnId = Guid.NewGuid();

        using (var conn = CreateFreshConnection())
        {
            await conn.ExecuteAsync("INSERT INTO transactions (id, account_id, amount_in_cents, type, created_at, occ_version) VALUES (@Id, @AccountId, @Amount, @Type, @CreatedAt, @OccVersion)",
                new { Id = lastTxnId.ToString(), AccountId = accountId.ToString(), Amount = 10000, Type = "C", CreatedAt = DateTime.UtcNow.ToString("o"), OccVersion = 1 });
        }

        var snapshot = new BalanceSnapshot(accountId, new Money(1000), lastTxnId, 1);

        var result = await _snapshotRepository.CreateAsync(snapshot, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(accountId, result.AccountId);
        Assert.Equal(new Money(1000), result.Balance);
        Assert.Equal(lastTxnId, result.LastTransactionId);
        Assert.Equal(1, result.OccVersion);
    }

    [Fact]
    public async Task SnapshotRepository_CreateAsync_UpdatesExistingSnapshot()
    {
        var accountId = await CreateAccountAsync();
        var lastTxnId1 = Guid.NewGuid();
        var lastTxnId2 = Guid.NewGuid();

        using (var conn = CreateFreshConnection())
        {
            await conn.ExecuteAsync("INSERT INTO transactions (id, account_id, amount_in_cents, type, created_at, occ_version) VALUES (@Id, @AccountId, @Amount, @Type, @CreatedAt, @OccVersion)",
                new { Id = lastTxnId1.ToString(), AccountId = accountId.ToString(), Amount = 10000, Type = "C", CreatedAt = DateTime.UtcNow.ToString("o"), OccVersion = 1 });
            await conn.ExecuteAsync("INSERT INTO transactions (id, account_id, amount_in_cents, type, created_at, occ_version) VALUES (@Id, @AccountId, @Amount, @Type, @CreatedAt, @OccVersion)",
                new { Id = lastTxnId2.ToString(), AccountId = accountId.ToString(), Amount = 10000, Type = "C", CreatedAt = DateTime.UtcNow.ToString("o"), OccVersion = 2 });
        }

        var snapshot1 = new BalanceSnapshot(accountId, new Money(1000), lastTxnId1, 1);
        await _snapshotRepository.CreateAsync(snapshot1, CancellationToken.None);

        var retrieved1 = await _snapshotRepository.GetLastAsync(accountId, CancellationToken.None);
        Assert.NotNull(retrieved1);

        var snapshot2 = new BalanceSnapshot(accountId, new Money(1500), lastTxnId2, retrieved1.OccVersion + 1);
        var result = await _snapshotRepository.CreateAsync(snapshot2, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new Money(1500), result.Balance);
        Assert.Equal(lastTxnId2, result.LastTransactionId);
        Assert.Equal(retrieved1.OccVersion + 1, result.OccVersion);
    }

    [Fact]
    public async Task SnapshotRepository_GetAccountsWithNewTransactionsAsync_ReturnsAccountsWithNewTransactions()
    {
        var accountId1 = await CreateAccountAsync("111111");
        var accountId2 = await CreateAccountAsync("222222");

        var t1 = await _transactionRepository.CreateAsync(new Transaction(accountId1, new Money(100), TransactionType.CREDIT, 1), CancellationToken.None);

        var snapshot = new BalanceSnapshot(accountId1, new Money(100), t1.Id, 1);
        await _snapshotRepository.CreateAsync(snapshot, CancellationToken.None);

        await _transactionRepository.CreateAsync(new Transaction(accountId1, new Money(50), TransactionType.CREDIT, 2), CancellationToken.None);

        var accounts = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(CancellationToken.None);

        Assert.Contains(accountId1, accounts);
        Assert.DoesNotContain(accountId2, accounts);
    }

    [Fact]
    public async Task SnapshotRepository_GetAccountsWithNewTransactionsAsync_ExcludesAccountsWithoutNewTransactions()
    {
        var accountId = await CreateAccountAsync("111111");

        var t1 = await _transactionRepository.CreateAsync(new Transaction(accountId, new Money(100), TransactionType.CREDIT, 1), CancellationToken.None);

        var snapshot = new BalanceSnapshot(accountId, new Money(100), t1.Id, 1);
        await _snapshotRepository.CreateAsync(snapshot, CancellationToken.None);

        var accounts = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(CancellationToken.None);

        Assert.DoesNotContain(accountId, accounts);
    }

    [Fact]
    public async Task Money_CentsConversion_WorksCorrectly()
    {
        var accountId = await CreateAccountAsync();

        var transaction = new Transaction(accountId, new Money(100.50m), TransactionType.CREDIT, 1);
        var created = await _transactionRepository.CreateAsync(transaction, CancellationToken.None);

        using var connection = CreateFreshConnection();
        var row = await connection.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT amount_in_cents FROM transactions WHERE id = @Id", new { Id = created.Id.ToString() });

        Assert.NotNull(row);
        Assert.Equal(10050L, (long)row!.amount_in_cents);
    }

    [Fact]
    public async Task TransactionRepository_CreateAsync_StoresCorrectType()
    {
        var accountId = await CreateAccountAsync();

        var creditTx = new Transaction(accountId, new Money(100), TransactionType.CREDIT, 1);
        var debitTx = new Transaction(accountId, new Money(50), TransactionType.DEBIT, 2);

        await _transactionRepository.CreateAsync(creditTx, CancellationToken.None);
        await _transactionRepository.CreateAsync(debitTx, CancellationToken.None);

        using var connection = CreateFreshConnection();
        var types = await connection.QueryAsync<string>(
            "SELECT type FROM transactions WHERE account_id = @AccountId ORDER BY occ_version",
            new { AccountId = accountId.ToString() });

        Assert.Equal(new[] { "C", "D" }, types);
    }
}