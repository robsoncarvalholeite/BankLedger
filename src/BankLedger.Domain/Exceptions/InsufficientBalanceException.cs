namespace BankLedger.Domain.Exceptions;

public sealed class InsufficientBalanceException : Exception
{
    public InsufficientBalanceException(decimal requested, decimal available)
        : base($"Insufficient balance on account. Requested: {requested:C}, Available: {available:C}")
    {
        Requested = requested;
        Available = available;
    }

    public decimal Requested { get; }
    public decimal Available { get; }
}
