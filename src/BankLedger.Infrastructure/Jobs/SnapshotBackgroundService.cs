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

        var accountIds = await snapshotRepository.GetAccountsWithNewTransactionsAsync(cancellationToken);

        foreach (var accountId in accountIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await CreateSnapshotForAccountAsync(accountId, transactionRepository, snapshotRepository, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating snapshot for account {AccountId}", accountId);
            }
        }
    }

    private async Task CreateSnapshotForAccountAsync(
        Guid accountId,
        ITransactionRepository transactionRepository,
        ISnapshotRepository snapshotRepository,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotRepository.GetAsync(accountId, cancellationToken);

        var lastTransactionId = snapshot?.LastTransactionId ?? Guid.Empty;
        var balance = snapshot?.Balance ?? new Money(0);
        var lastOccVersion = snapshot?.OccVersion ?? 0;

        var delta = await transactionRepository.GetBalanceDeltaAsync(accountId, lastTransactionId, null, cancellationToken);
        var newBalance = balance + delta;

        var latestTransactionId = await GetLatestTransactionIdAsync(accountId, cancellationToken);
        if (latestTransactionId == Guid.Empty)
            return;

        var newOccVersion = lastOccVersion + 1;

        var newSnapshot = new BalanceSnapshot(accountId, newBalance, latestTransactionId, newOccVersion);

        await snapshotRepository.CreateAsync(newSnapshot, cancellationToken);

        _logger.LogInformation("Created snapshot for account {AccountId} with occ_version {OccVersion}", accountId, newOccVersion);
    }

    private async Task<Guid> GetLatestTransactionIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        var result = await connection.ExecuteScalarAsync<string>(
            "SELECT id FROM transactions WHERE account_id = @AccountId ORDER BY created_at DESC LIMIT 1",
            new { AccountId = accountId.ToString() });

        return result != null ? Guid.Parse(result) : Guid.Empty;
    }
}