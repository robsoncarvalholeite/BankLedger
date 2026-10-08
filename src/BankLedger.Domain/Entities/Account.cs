namespace BankLedger.Domain.Entities;

public sealed class Account
{
    public Guid Id { get; private set; }
    public string Number { get; private set; }

    public Account(Guid id, string number)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Account ID is required", nameof(id));
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Account number is required", nameof(number));

        Id = id;
        Number = number;
    }

    public static Account Create(string number)
        => new Account(Guid.CreateVersion7(), number);
}