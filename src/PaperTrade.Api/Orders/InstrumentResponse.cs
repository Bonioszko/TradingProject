namespace PaperTrade.Api.Orders;

public sealed record InstrumentResponse(
    Guid InstrumentId,
    string Symbol,
    string Name,
    decimal MarketPrice,
    string Currency);
