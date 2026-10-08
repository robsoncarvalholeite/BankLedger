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
    private readonly ILogger<SnapshotBackgroundService> _logger;
    private readonly TimeSpan _interval;

    private readonly ITransactionRepository _transactionRepository;
    private readonly ISnapshotRepository _snapshotRepository;

    public SnapshotBackgroundService(
        IServiceScopeFactory scopeFactory,
        SqliteConnectionFactory connectionFactory,
        ILogger<SnapshotBackgroundService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        using var scope = scopeFactory.CreateScope();
        _transactionRepository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        _snapshotRepository = scope.ServiceProvider.GetRequiredService<ISnapshotRepository>();

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

    private async Task ProcessSnapshotsAsync(CancellationToken ct)
    {


        var accountIds = await _snapshotRepository.GetAccountsWithNewTransactionsAsync(ct);

        Console.WriteLine($"Found {accountIds.Count} accounts with new transactions.");

        foreach (var accountId in accountIds)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await CreateSnapshotForAccountAsync(accountId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating snapshot for account {AccountId}", accountId);
            }
        }
    }

    private async Task CreateSnapshotForAccountAsync(
        Guid accountId,
        CancellationToken ct)
    {
        var snapshot = await _snapshotRepository.GetLastAsync(accountId, ct);

        var transactionId = snapshot?.LastTransactionId ?? Guid.Empty;
        var balance = snapshot?.Balance ?? new Money(0);
        var lastOccVersion = snapshot?.OccVersion ?? 0;

        var (delta, lastTransactionId) = await _transactionRepository.GetBalanceDeltaAsync(accountId, transactionId, null, ct);
        var newBalance = balance + delta;

        var newOccVersion = lastOccVersion + 1;

        var newSnapshot = new BalanceSnapshot(accountId, newBalance, lastTransactionId, newOccVersion);

        await _snapshotRepository.CreateAsync(newSnapshot, ct);

        _logger.LogInformation("Created snapshot for account {AccountId} with occ_version {OccVersion}", accountId, newOccVersion);
    }
}
