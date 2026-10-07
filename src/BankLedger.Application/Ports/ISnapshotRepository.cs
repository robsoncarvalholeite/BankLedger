using BankLedger.Domain.Entities;

namespace BankLedger.Application.Ports;

public interface ISnapshotRepository
{
    Task<BalanceSnapshot?> GetAsync(
        string accountNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAccountsWithNewTransactionsAsync(
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        BalanceSnapshot snapshot,
        long expectedSequence,
        CancellationToken cancellationToken);
}