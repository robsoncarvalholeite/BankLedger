namespace BankLedger.Domain.Exceptions;

public sealed class InsufficientBalanceException : Exception
{
    public InsufficientBalanceException(string accountNumber, decimal requested, decimal available)
        : base($"Insufficient balance for account {accountNumber}. Requested: {requested:C}, Available: {available:C}")
    {
        AccountNumber = accountNumber;
        Requested = requested;
        Available = available;
    }

    public string AccountNumber { get; }
    public decimal Requested { get; }
    public decimal Available { get; }
}