using BankLedger.Domain.Entities;
using BankLedger.Domain.ValueObjects;

namespace BankLedger.Application.Ports;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken);

    Task<Money> GetBalanceDeltaAsync(
        string accountNumber,
        long lastTransactionId,
        DateTime? until,
        CancellationToken cancellationToken);

    Task<Transaction> CreateAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}