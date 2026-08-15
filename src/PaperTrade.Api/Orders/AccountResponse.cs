namespace PaperTrade.Api.Orders;

public sealed record AccountResponse(
    Guid AccountId,
    decimal AvailableCash,
    string Currency,
    long Version);
