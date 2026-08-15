using PaperTrade.Domain.Orders;

namespace PaperTrade.Api.Orders;

public sealed record OrderResponse(
    Guid OrderId,
    Guid AccountId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? LimitPrice,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FilledAt,
    string? RejectionReason,
    ExecutionResponse? Execution);

public sealed record ExecutionResponse(
    Guid ExecutionId,
    decimal Quantity,
    decimal Price,
    decimal Commission,
    DateTimeOffset ExecutedAt);
