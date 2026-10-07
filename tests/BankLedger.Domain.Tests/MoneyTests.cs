using BankLedger.Domain.ValueObjects;

namespace BankLedger.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(1.005, 1.00)]
    [InlineData(1.015, 1.02)]
    [InlineData(2.125, 2.12)]
    [InlineData(2.135, 2.14)]
    public void Rounding_UsesBankersRounding(decimal input, decimal expected)
    {
        var money = new Money(input);

        Assert.Equal(expected, money.Amount);
    }

    [Theory]
    [InlineData(10.10, 5.20, 15.30)]
    [InlineData(0.01, 0.01, 0.02)]
    public void Addition_ReturnsNormalizedMoney(decimal left, decimal right, decimal expected)
    {
        var result = new Money(left) + new Money(right);

        Assert.Equal(expected, result.Amount);
    }

    [Theory]
    [InlineData(10.00, 3.25, 6.75)]
    [InlineData(3.00, 5.00, -2.00)]
    public void Subtraction_ReturnsNormalizedMoney(decimal left, decimal right, decimal expected)
    {
        var result = new Money(left) - new Money(right);

        Assert.Equal(expected, result.Amount);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10.50, -10.50)]
    [InlineData(0.001, 0)]
    [InlineData(0.005, 0)]
    [InlineData(999999999.999, 1000000000.00)]
    public void EdgeCases_HandleCorrectly(decimal input, decimal expected)
    {
        var money = new Money(input);

        Assert.Equal(expected, money.Amount);
    }
}