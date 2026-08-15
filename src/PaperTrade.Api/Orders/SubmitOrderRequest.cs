using PaperTrade.Domain.Orders;

namespace PaperTrade.Api.Orders;

public sealed record SubmitOrderRequest(
    Guid AccountId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice);
