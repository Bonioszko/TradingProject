using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaperTrade.Contracts.Orders;
using PaperTrade.Domain.Orders;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.Orders;

public sealed class OrderSubmissionService(
    TradingDbContext context,
    TimeProvider timeProvider)
{
    public async Task<OrderSubmissionResult> SubmitAsync(
        SubmitOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (request.Side != OrderSide.Buy || request.Type != OrderType.Market)
        {
            return OrderSubmissionResult.Unsupported(
                "Phase 3 supports market buy orders only.");
        }

        var normalizedKey = idempotencyKey.Trim();
        var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();
        var requestHash = ComputeRequestHash(request, normalizedSymbol);
        var replay = await ResolveExistingAsync(normalizedKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var instrument = await context.Instruments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Symbol == normalizedSymbol,
                cancellationToken);
        if (instrument is null)
        {
            return OrderSubmissionResult.NotFound($"Instrument '{normalizedSymbol}' was not found.");
        }

        var account = await context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.AccountId, cancellationToken);
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
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope<OrderSubmittedV1>(
            new MessageMetadata(
                messageId,
                order.Id,
                CausationId: null,
                OrderContractNames.SubmittedV1,
                OrderContractNames.Version1,
                now,
                Activity.Current?.Id),
            new OrderSubmittedV1(
                order.Id,
                order.AccountId,
                order.InstrumentId,
                ToContractSide(order.Side),
                ToContractType(order.Type),
                order.Quantity,
                order.LimitPrice,
                order.CreatedAt));

        context.Orders.Add(order);
        context.HttpIdempotencyRequests.Add(
            HttpIdempotencyRequest.Create(normalizedKey, requestHash, order.Id, now));
        context.OutboxMessages.Add(
            OutboxMessage.Create(
                messageId,
                order.Id,
                order.Id,
                OrderContractNames.SubmittedV1,
                OrderContractNames.Version1,
                RabbitMqTopology.CommandExchange,
                RabbitMqTopology.OrderSubmittedRoutingKey,
                MessageJson.Serialize(envelope),
                now,
                Activity.Current?.Id));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            context.ChangeTracker.Clear();
            var concurrentReplay = await ResolveExistingAsync(
                normalizedKey,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                return concurrentReplay;
            }

            throw;
        }

        return OrderSubmissionResult.Accepted(ToResponse(order, instrument.Symbol), isReplay: false);
    }

    public Task<OrderResponse?> FindOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        QueryOrders(orderId).SingleOrDefaultAsync(cancellationToken);

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

    private async Task<OrderSubmissionResult?> ResolveExistingAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await context.HttpIdempotencyRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Key == idempotencyKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.RequestHash),
                Convert.FromHexString(requestHash)))
        {
            return OrderSubmissionResult.IdempotencyConflict(
                "The idempotency key has already been used for a different request.");
        }

        var order = await FindOrderAsync(existing.OrderId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Idempotency record '{idempotencyKey}' references a missing order.");
        return OrderSubmissionResult.Accepted(order, isReplay: true);
    }

    private IQueryable<OrderResponse> QueryOrders(Guid orderId)
    {
        var orders = context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId);

        return
        from order in orders
        join instrument in context.Instruments.AsNoTracking()
            on order.InstrumentId equals instrument.Id
        join execution in context.Executions.AsNoTracking()
            on order.Id equals execution.OrderId into executions
        from execution in executions.DefaultIfEmpty()
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
                    execution.ExecutedAt));
    }

    private static OrderResponse ToResponse(Order order, string symbol) =>
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
            Execution: null);

    private static string ComputeRequestHash(SubmitOrderRequest request, string normalizedSymbol)
    {
        var canonical = string.Join(
            '|',
            request.AccountId.ToString("D"),
            normalizedSymbol,
            ((int)request.Side).ToString(CultureInfo.InvariantCulture),
            ((int)request.Type).ToString(CultureInfo.InvariantCulture),
            request.Quantity.ToString("G29", CultureInfo.InvariantCulture),
            request.LimitPrice?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        };

    private static OrderSideV1 ToContractSide(OrderSide side) => side switch
    {
        OrderSide.Buy => OrderSideV1.Buy,
        OrderSide.Sell => OrderSideV1.Sell,
        _ => throw new UnreachableException()
    };

    private static OrderTypeV1 ToContractType(OrderType type) => type switch
    {
        OrderType.Market => OrderTypeV1.Market,
        OrderType.Limit => OrderTypeV1.Limit,
        _ => throw new UnreachableException()
    };
}

public sealed record OrderSubmissionResult(
    OrderResponse? Order,
    OrderSubmissionError Error,
    string? ErrorMessage,
    bool IsReplay)
{
    public static OrderSubmissionResult Accepted(OrderResponse order, bool isReplay) =>
        new(order, OrderSubmissionError.None, ErrorMessage: null, isReplay);

    public static OrderSubmissionResult NotFound(string message) =>
        new(Order: null, OrderSubmissionError.NotFound, message, IsReplay: false);

    public static OrderSubmissionResult Unsupported(string message) =>
        new(Order: null, OrderSubmissionError.Unsupported, message, IsReplay: false);

    public static OrderSubmissionResult IdempotencyConflict(string message) =>
        new(Order: null, OrderSubmissionError.IdempotencyConflict, message, IsReplay: false);
}

public enum OrderSubmissionError
{
    None = 0,
    NotFound = 1,
    Unsupported = 2,
    IdempotencyConflict = 3
}
