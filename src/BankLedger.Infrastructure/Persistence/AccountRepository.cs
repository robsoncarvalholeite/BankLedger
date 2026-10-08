using Dapper;
using BankLedger.Domain.Ports;
using BankLedger.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace BankLedger.Infrastructure.Persistence;

public sealed class AccountRepository : IAccountRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AccountRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Account?> GetByNumberAsync(string number, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AccountRow>(
            "SELECT id, number FROM accounts WHERE number = @Number",
            new { Number = number });
        return row?.ToDomain();
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<AccountRow>(
            "SELECT id, number FROM accounts WHERE id = @Id",
            new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<Account> CreateAsync(Account account, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "INSERT INTO accounts (id, number) VALUES (@Id, @Number)",
            new { Id = account.Id.ToString(), Number = account.Number });
        return account;
    }

    private sealed record AccountRow(string Id, string Number)
    {
        public Account ToDomain()
            => new Account(Guid.Parse(Id), Number);
    }
}