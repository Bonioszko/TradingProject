using Microsoft.EntityFrameworkCore;
using PaperTrade.Domain.Orders;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.Orders;

public sealed class SynchronousOrderService(
    TradingDbContext context,
    TimeProvider timeProvider)
{
    public async Task<OrderSubmissionResult> SubmitAsync(
        SubmitOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Side != OrderSide.Buy || request.Type != OrderType.Market)
        {
            return OrderSubmissionResult.Unsupported(
                "Phase 2 supports market buy orders only.");
        }

        var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();
        var instrument = await context.Instruments
            .SingleOrDefaultAsync(
                candidate => candidate.Symbol == normalizedSymbol,
                cancellationToken);

        if (instrument is null)
        {
            return OrderSubmissionResult.NotFound($"Instrument '{normalizedSymbol}' was not found.");
        }

        var account = await context.Accounts.FindAsync([request.AccountId], cancellationToken);
        if (account is null)
        {
            return OrderSubmissionResult.NotFound($"Account '{request.AccountId}' was not found.");
        }

        if (!string.Equals(account.Currency, instrument.Currency, StringComparison.Ordinal))
        {
            return OrderSubmissionResult.Unsupported(
                $"Account currency {account.Currency} cannot trade {instrument.Currency} instruments.");
        }

        var now = timeProvider.GetUtcNow();
        var order = Order.Place(
            Guid.NewGuid(),
            account.Id,
            instrument.Id,
            request.Side,
            request.Type,
            request.Quantity,
            request.LimitPrice,
            now);

        order.Accept();
        context.Orders.Add(order);

        const decimal commission = 0m;
        var requiredCash = decimal.Round(
            (instrument.MarketPrice * order.Quantity) + commission,
            4,
            MidpointRounding.ToEven);

        if (!account.HasSufficientFunds(requiredCash))
        {
            order.Reject(
                $"Insufficient funds: {requiredCash:F2} {account.Currency} required, " +
                $"{account.AvailableCash:F2} {account.Currency} available.");

            await context.SaveChangesAsync(cancellationToken);
            return OrderSubmissionResult.Completed(ToResponse(order, instrument.Symbol, execution: null));
        }

        account.Debit(requiredCash);
        var execution = order.Fill(
            Guid.NewGuid(),
            instrument.MarketPrice,
            commission,
            now);

        context.Executions.Add(execution);
        await context.SaveChangesAsync(cancellationToken);

        return OrderSubmissionResult.Completed(ToResponse(order, instrument.Symbol, execution));
    }

    public Task<OrderResponse?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        (
            from order in context.Orders.AsNoTracking()
            join instrument in context.Instruments.AsNoTracking()
                on order.InstrumentId equals instrument.Id
            join execution in context.Executions.AsNoTracking()
                on order.Id equals execution.OrderId into executions
            from execution in executions.DefaultIfEmpty()
            where order.Id == orderId
            select new OrderResponse(
                order.Id,
                order.AccountId,
                instrument.Symbol,
                order.Side,
                order.Type,
                order.Quantity,
                order.LimitPrice,
                order.Status,
                order.CreatedAt,
                order.FilledAt,
                order.RejectionReason,
                execution == null
                    ? null
                    : new ExecutionResponse(
                        execution.Id,
                        execution.Quantity,
                        execution.Price,
                        execution.Commission,
                        execution.ExecutedAt)))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<AccountResponse?> FindAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        context.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Select(account => new AccountResponse(
                account.Id,
                account.AvailableCash,
                account.Currency,
                account.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<List<InstrumentResponse>> ListInstrumentsAsync(CancellationToken cancellationToken) =>
        context.Instruments
            .AsNoTracking()
            .OrderBy(instrument => instrument.Symbol)
            .Select(instrument => new InstrumentResponse(
                instrument.Id,
                instrument.Symbol,
                instrument.Name,
                instrument.MarketPrice,
                instrument.Currency))
            .ToListAsync(cancellationToken);

    private static OrderResponse ToResponse(
        Order order,
        string symbol,
        Domain.Executions.Execution? execution) =>
        new(
            order.Id,
            order.AccountId,
            symbol,
            order.Side,
            order.Type,
            order.Quantity,
            order.LimitPrice,
            order.Status,
            order.CreatedAt,
            order.FilledAt,
            order.RejectionReason,
            execution is null
                ? null
                : new ExecutionResponse(
                    execution.Id,
                    execution.Quantity,
                    execution.Price,
                    execution.Commission,
                    execution.ExecutedAt));
}

public sealed record OrderSubmissionResult(
    OrderResponse? Order,
    OrderSubmissionError Error,
    string? ErrorMessage)
{
    public static OrderSubmissionResult Completed(OrderResponse order) =>
        new(order, OrderSubmissionError.None, ErrorMessage: null);

    public static OrderSubmissionResult NotFound(string message) =>
        new(Order: null, OrderSubmissionError.NotFound, message);

    public static OrderSubmissionResult Unsupported(string message) =>
        new(Order: null, OrderSubmissionError.Unsupported, message);
}

public enum OrderSubmissionError
{
    None = 0,
    NotFound = 1,
    Unsupported = 2
}
