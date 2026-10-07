namespace BankLedger.Domain.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }

    public Money(decimal amount)
    {
        Amount = Round(amount);
    }

    public static Money operator +(Money left, Money right)
        => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right)
        => new(left.Amount - right.Amount);

    // Banker's Rounding Rule: Round to the nearest even number when the value is exactly halfway between two numbers.
    private static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.ToEven);
}
