using System.Threading;

namespace BankLedger.Domain.Ports;

public interface IdempotencyLock
{
    Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken ct);
    Task AddAsync(Guid idempotencyKey, CancellationToken ct);
}