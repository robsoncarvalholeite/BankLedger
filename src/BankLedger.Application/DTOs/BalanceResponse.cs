namespace BankLedger.Application.DTOs;

public sealed class BalanceResponse
{
    public required string AccountNumber { get; init; }
    public required decimal Balance { get; init; }
    public required DateTime AsOf { get; init; }
}