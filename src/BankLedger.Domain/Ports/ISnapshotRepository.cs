using BankLedger.Domain.Entities;

namespace BankLedger.Domain.Ports;

public interface ISnapshotRepository
{
    Task<BalanceSnapshot?> GetLastAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetAccountsWithNewTransactionsAsync(
        CancellationToken cancellationToken);

    Task<BalanceSnapshot> CreateAsync(
        BalanceSnapshot snapshot,
        CancellationToken cancellationToken);
}
