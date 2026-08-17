using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaperTrade.Contracts.Orders;
using PaperTrade.Domain.Orders;
using PaperTrade.Messaging;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Execution.Worker;

public sealed class OrderExecutor(
    TradingDbContext context,
    TimeProvider timeProvider)
{
    private const string ConsumerName = "execution-worker-v1";

    public async Task<OrderExecutionOutcome> ExecuteAsync(
        MessageEnvelope<OrderSubmittedV1> envelope,
        CancellationToken cancellationToken)
    {
        ValidateEnvelope(envelope);

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            () => ExecuteTransactionAsync(envelope, cancellationToken));
    }

    private async Task<OrderExecutionOutcome> ExecuteTransactionAsync(
        MessageEnvelope<OrderSubmittedV1> envelope,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.InboxMessages.AnyAsync(
                message => message.MessageId == envelope.Metadata.MessageId,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return OrderExecutionOutcome.Duplicate;
        }

        var order = await context.Orders.SingleOrDefaultAsync(
            candidate => candidate.Id == envelope.Payload.OrderId,
            cancellationToken);
        if (order is null)
        {
            throw new InvalidOrderMessageException(
                $"Order '{envelope.Payload.OrderId}' does not exist.");
        }

        if (order.AccountId != envelope.Payload.AccountId
            || order.InstrumentId != envelope.Payload.InstrumentId)
        {
            throw new InvalidOrderMessageException(
                $"Message '{envelope.Metadata.MessageId}' does not match persisted order identifiers.");
        }

        var now = timeProvider.GetUtcNow();
        context.InboxMessages.Add(
            InboxMessage.Processed(envelope.Metadata.MessageId, ConsumerName, now));

        if (order.Status is OrderStatus.Filled or OrderStatus.Rejected)
        {
            await SaveAndCommitAsync(transaction, cancellationToken);
            return OrderExecutionOutcome.Duplicate;
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new InvalidOrderMessageException(
                $"Order '{order.Id}' is in unsupported state '{order.Status}'.");
        }

        var account = await context.Accounts.SingleOrDefaultAsync(
            candidate => candidate.Id == order.AccountId,
            cancellationToken);
        var instrument = await context.Instruments.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == order.InstrumentId,
            cancellationToken);
        if (account is null || instrument is null)
        {
            throw new InvalidOrderMessageException(
                $"Order '{order.Id}' references missing authoritative data.");
        }

        const decimal commission = 0m;
        Domain.Executions.Execution? execution = null;

        if (order.Side != OrderSide.Buy || order.Type != OrderType.Market)
        {
            order.Reject("Only market buy orders are currently supported.");
        }
        else if (!string.Equals(account.Currency, instrument.Currency, StringComparison.Ordinal))
        {
            order.Reject(
                $"Account currency {account.Currency} cannot trade {instrument.Currency} instruments.");
        }
        else
        {
            order.Accept();
            var requiredCash = decimal.Round(
                (instrument.MarketPrice * order.Quantity) + commission,
                4,
                MidpointRounding.ToEven);

            if (!account.HasSufficientFunds(requiredCash))
            {
                order.Reject(
                    $"Insufficient funds: {requiredCash:F2} {account.Currency} required, " +
                    $"{account.AvailableCash:F2} {account.Currency} available.");
            }
            else
            {
                account.Debit(requiredCash);
                execution = order.Fill(
                    Guid.NewGuid(),
                    instrument.MarketPrice,
                    commission,
                    now);
                context.Executions.Add(execution);
            }
        }

        AddResultToOutbox(envelope, order, execution, now);

        try
        {
            await SaveAndCommitAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsInboxDuplicate(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return OrderExecutionOutcome.Duplicate;
        }

        return order.Status == OrderStatus.Filled
            ? OrderExecutionOutcome.Filled
            : OrderExecutionOutcome.Rejected;
    }

    private async Task SaveAndCommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddResultToOutbox(
        MessageEnvelope<OrderSubmittedV1> submitted,
        Order order,
        Domain.Executions.Execution? execution,
        DateTimeOffset now)
    {
        var resultStatus = order.Status switch
        {
            OrderStatus.Filled => OrderResultStatusV1.Filled,
            OrderStatus.Rejected => OrderResultStatusV1.Rejected,
            _ => throw new InvalidOperationException($"Order '{order.Id}' is not terminal.")
        };
        var routingKey = resultStatus == OrderResultStatusV1.Filled
            ? RabbitMqTopology.OrderFilledRoutingKey
            : RabbitMqTopology.OrderRejectedRoutingKey;
        var resultMessageId = Guid.NewGuid();
        var result = new OrderResultV1(
            order.Id,
            order.AccountId,
            resultStatus,
            execution?.Id,
            execution?.Quantity,
            execution?.Price,
            execution?.Commission,
            now,
            order.RejectionReason);
        var envelope = new MessageEnvelope<OrderResultV1>(
            new MessageMetadata(
                resultMessageId,
                submitted.Metadata.CorrelationId,
                submitted.Metadata.MessageId,
                OrderContractNames.ResultV1,
                OrderContractNames.Version1,
                now,
                System.Diagnostics.Activity.Current?.Id ?? submitted.Metadata.TraceParent),
            result);

        context.OutboxMessages.Add(
            OutboxMessage.Create(
                resultMessageId,
                order.Id,
                submitted.Metadata.CorrelationId,
                OrderContractNames.ResultV1,
                OrderContractNames.Version1,
                RabbitMqTopology.EventExchange,
                routingKey,
                MessageJson.Serialize(envelope),
                now,
                envelope.Metadata.TraceParent));
    }

    private static void ValidateEnvelope(MessageEnvelope<OrderSubmittedV1> envelope)
    {
        if (envelope.Metadata.MessageId == Guid.Empty
            || envelope.Metadata.CorrelationId == Guid.Empty
            || envelope.Metadata.ContractType != OrderContractNames.SubmittedV1
            || envelope.Metadata.ContractVersion != OrderContractNames.Version1
            || envelope.Payload.OrderId == Guid.Empty)
        {
            throw new InvalidOrderMessageException("The order-submitted envelope is invalid.");
        }
    }

    private static bool IsInboxDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var constraintName
        }
        && constraintName?.Contains("inbox_messages", StringComparison.OrdinalIgnoreCase) == true;
}

public enum OrderExecutionOutcome
{
    Filled = 1,
    Rejected = 2,
    Duplicate = 3
}

public sealed class InvalidOrderMessageException(string message) : Exception(message);
