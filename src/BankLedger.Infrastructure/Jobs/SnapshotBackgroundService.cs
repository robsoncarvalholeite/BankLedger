using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;
using BankLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dapper;

namespace BankLedger.Infrastructure.Jobs;

public sealed class SnapshotBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SnapshotBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public SnapshotBackgroundService(
        IServiceScopeFactory scopeFactory,
        SqliteConnectionFactory connectionFactory,
        ILogger<SnapshotBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _connectionFactory = connectionFactory;
        _logger = logger;

        var intervalMinutes = configuration.GetValue<int>("SNAPSHOT_INTERVAL_MINUTES", 5);
        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Snapshot background service started with interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSnapshotsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing snapshots");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var transactionRepository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var snapshotRepository = scope.ServiceProvider.GetRequiredService<ISnapshotRepository>();

        var accounts = await snapshotRepository.GetAccountsWithNewTransactionsAsync(cancellationToken);

        foreach (var accountNumber in accounts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await UpdateSnapshotForAccountAsync(accountNumber, transactionRepository, snapshotRepository, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating snapshot for account {AccountNumber}", accountNumber);
            }
        }
    }

    private async Task UpdateSnapshotForAccountAsync(
        string accountNumber,
        ITransactionRepository transactionRepository,
        ISnapshotRepository snapshotRepository,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotRepository.GetAsync(accountNumber, cancellationToken);

        long lastTransactionId = 0;
        Money balance = new Money(0);
        long expectedSequence = 0;

        if (snapshot is not null)
        {
            lastTransactionId = snapshot.LastTransactionId;
            balance = snapshot.Balance;
            expectedSequence = snapshot.Sequence;
        }

        var delta = await transactionRepository.GetBalanceDeltaAsync(accountNumber, lastTransactionId, null, cancellationToken);
        var newBalance = balance + delta;

        var latestTransaction = await GetLatestTransactionIdAsync(accountNumber, cancellationToken);
        if (latestTransaction == 0)
            return;

        var newSnapshot = snapshot is null
            ? new BalanceSnapshot(accountNumber, newBalance, latestTransaction, 0)
            : snapshot.Update(newBalance, latestTransaction);

        var success = await snapshotRepository.UpdateAsync(newSnapshot, expectedSequence, cancellationToken);

        if (!success)
        {
            _logger.LogWarning("Concurrency conflict updating snapshot for account {AccountNumber}, retrying next cycle", accountNumber);
        }
    }

    private async Task<long> GetLatestTransactionIdAsync(string accountNumber, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(Id), 0) FROM Transactions WHERE AccountNumber = @AccountNumber",
            new { AccountNumber = accountNumber });
    }
}