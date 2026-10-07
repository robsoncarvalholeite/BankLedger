using BankLedger.Application.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;
using BankLedger.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Dapper;

namespace BankLedger.Infrastructure.Jobs;

public sealed class SnapshotBackgroundService : BackgroundService
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SnapshotBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public SnapshotBackgroundService(
        ITransactionRepository transactionRepository,
        ISnapshotRepository snapshotRepository,
        SqliteConnectionFactory connectionFactory,
        ILogger<SnapshotBackgroundService> logger,
        IConfiguration configuration)
    {
        _transactionRepository = transactionRepository;
        _snapshotRepository = snapshotRepository;
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
        var accounts = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(cancellationToken);

        foreach (var accountNumber in accounts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await UpdateSnapshotForAccountAsync(accountNumber, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating snapshot for account {AccountNumber}", accountNumber);
            }
        }
    }

    private async Task UpdateSnapshotForAccountAsync(string accountNumber, CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotRepository.GetAsync(accountNumber, cancellationToken);
        
        long lastTransactionId = 0;
        Money balance = new Money(0);
        long expectedSequence = 0;

        if (snapshot is not null)
        {
            lastTransactionId = snapshot.LastTransactionId;
            balance = snapshot.Balance;
            expectedSequence = snapshot.Sequence;
        }

        var delta = await _transactionRepository.GetBalanceDeltaAsync(accountNumber, lastTransactionId, null, cancellationToken);
        var newBalance = balance + delta;

        var latestTransaction = await GetLatestTransactionIdAsync(accountNumber, cancellationToken);
        if (latestTransaction == 0)
            return;

        var newSnapshot = snapshot is null
            ? new BalanceSnapshot(accountNumber, newBalance, latestTransaction, 0)
            : snapshot.Update(newBalance, latestTransaction);

        var success = await _snapshotRepository.UpdateAsync(newSnapshot, expectedSequence, cancellationToken);
        
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