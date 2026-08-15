namespace PaperTrade.Domain.Accounts;

public sealed class Account
{
    private Account()
    {
    }

    private Account(Guid id, decimal availableCash, string currency)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(id));
        }

        if (availableCash < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableCash), "Available cash cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("A currency is required.", nameof(currency));
        }

        Id = id;
        AvailableCash = availableCash;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public Guid Id { get; private set; }

    public decimal AvailableCash { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public long Version { get; private set; }

    public static Account Open(Guid id, decimal availableCash, string currency) =>
        new(id, availableCash, currency);

    public bool HasSufficientFunds(decimal amount) => amount > 0 && AvailableCash >= amount;

    public void Debit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A debit must be greater than zero.");
        }

        if (!HasSufficientFunds(amount))
        {
            throw new InvalidOperationException(
                $"Account {Id} has {AvailableCash:F2} {Currency}, but {amount:F2} {Currency} is required.");
        }

        AvailableCash -= amount;
        Version++;
    }
}
