using BankLedger.Domain.Entities;
using System.Threading;

namespace BankLedger.Domain.Ports;

public interface IAccountRepository
{
    Task<Account?> GetByNumberAsync(string number, CancellationToken ct);
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Account> CreateAsync(Account account, CancellationToken ct);
}