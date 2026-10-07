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
        Guid accountId,
        Guid? lastTransactionId,
        CancellationToken ct)
    {
        var snapshot = await _snapshotRepository.GetAsync(accountId, ct);

        var fromTxnId = lastTransactionId ?? snapshot?.LastTransactionId ?? Guid.Empty;

        var delta = await _transactionRepository.GetBalanceDeltaAsync(
            accountId,
            fromTxnId,
            null,
            ct);

        return (snapshot?.Balance ?? new Money(0)) + delta;
    }
}
