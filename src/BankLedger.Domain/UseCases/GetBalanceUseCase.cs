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
        CancellationToken ct)
    {
        var snapshot = await _snapshotRepository.GetAsync(accountNumber, ct);

        if (snapshot is not null)
        {
            Money delta = await _transactionRepository.GetBalanceDeltaAsync(
                accountNumber,
                snapshot.LastTransactionId,
                from,
                ct);
            return snapshot.Balance + delta;
        }
        return await _transactionRepository.GetBalanceDeltaAsync(
            accountNumber,
            0,
            from,
            ct);
    }
}
