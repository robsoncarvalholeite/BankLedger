using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.UseCases;

public sealed class GetBalanceUseCase
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly ISnapshotRepository _snapshotRepository;

    public GetBalanceUseCase(
        ITransactionRepository transactionRepository,
        ISnapshotRepository snapshotRepository)
    {
        _transactionRepository = transactionRepository;
        _snapshotRepository = snapshotRepository;
    }

    public async Task<Money> ExecuteAsync(
        string accountNumber,
        DateTime? from,
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotRepository.GetAsync(accountNumber, cancellationToken);

        Money balance;
        long lastTransactionId = 0;

        if (snapshot is not null)
        {
            balance = snapshot.Balance;
            lastTransactionId = snapshot.LastTransactionId;
        }
        else
        {
            balance = new Money(0);
        }

        var delta = await _transactionRepository.GetBalanceDeltaAsync(
            accountNumber,
            lastTransactionId,
            from,
            cancellationToken);

        return balance + delta;
    }
}