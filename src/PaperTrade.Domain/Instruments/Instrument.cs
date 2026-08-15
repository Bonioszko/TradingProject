namespace PaperTrade.Domain.Instruments;

public sealed class Instrument
{
    private Instrument()
    {
    }

    private Instrument(Guid id, string symbol, string name, decimal marketPrice, string currency)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An instrument ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("A symbol is required.", nameof(symbol));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An instrument name is required.", nameof(name));
        }

        if (marketPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marketPrice), "A market price must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("A currency is required.", nameof(currency));
        }

        Id = id;
        Symbol = symbol.Trim().ToUpperInvariant();
        Name = name.Trim();
        MarketPrice = marketPrice;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public Guid Id { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal MarketPrice { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public static Instrument Create(
        Guid id,
        string symbol,
        string name,
        decimal marketPrice,
        string currency) =>
        new(id, symbol, name, marketPrice, currency);
}
