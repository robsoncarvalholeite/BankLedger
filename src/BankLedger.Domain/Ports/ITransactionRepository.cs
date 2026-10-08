using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Ports;

public interface ITransactionRepository
{
    Task<(Money balance, Guid lastTransactionId)> GetBalanceDeltaAsync(
        Guid accountId,
        Guid transactionId,
        DateTime? until,
        CancellationToken cancellationToken);

    Task<Transaction> CreateAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}
