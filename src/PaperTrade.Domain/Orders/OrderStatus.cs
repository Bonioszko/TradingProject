namespace PaperTrade.Domain.Orders;

public enum OrderStatus
{
    Pending = 1,
    Accepted = 2,
    Filled = 3,
    Rejected = 4,
    Cancelled = 5
}
