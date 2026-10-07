namespace BankLedger.Domain.Entities;

public sealed class Account
{
    public string Number { get; private set; }

    public Account(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Account number is required", nameof(number));

        Number = number;
    }
}