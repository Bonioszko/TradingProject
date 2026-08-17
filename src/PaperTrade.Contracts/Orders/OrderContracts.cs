namespace PaperTrade.Contracts.Orders;

public static class OrderContractNames
{
    public const string SubmittedV1 = "papertrade.orders.submitted";
    public const string ResultV1 = "papertrade.orders.result";
    public const int Version1 = 1;
}

public sealed record OrderSubmittedV1(
    Guid OrderId,
    Guid AccountId,
    Guid InstrumentId,
    OrderSideV1 Side,
    OrderTypeV1 Type,
    decimal Quantity,
    decimal? LimitPrice,
    DateTimeOffset SubmittedAt);

public sealed record OrderResultV1(
    Guid OrderId,
    Guid AccountId,
    OrderResultStatusV1 Status,
    Guid? ExecutionId,
    decimal? ExecutedQuantity,
    decimal? ExecutedPrice,
    decimal? Commission,
    DateTimeOffset CompletedAt,
    string? RejectionReason);

public enum OrderSideV1
{
    Buy = 1,
    Sell = 2
}

public enum OrderTypeV1
{
    Market = 1,
    Limit = 2
}

public enum OrderResultStatusV1
{
    Filled = 1,
    Rejected = 2
}
