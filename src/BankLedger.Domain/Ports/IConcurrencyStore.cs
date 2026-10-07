using System.Threading;

namespace BankLedger.Domain.Ports;

public interface IConcurrencyStore
{
    Task<long> NextAsync(string accountNumber, CancellationToken ct);
}
