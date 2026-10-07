using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Ports;

public interface ITransactionRepository
{
    Task<Money> GetBalanceDeltaAsync(
        string accountNumber,
        long lastTransactionId,
        DateTime? until,
        CancellationToken cancellationToken);

    Task<Transaction> CreateAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}