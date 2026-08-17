using Microsoft.EntityFrameworkCore;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.Messaging;

public sealed class TradingOutboxStore(TradingDbContext context)
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        Guid leaseId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var messages = await context.OutboxMessages
                .FromSqlInterpolated($$"""
                    SELECT *
                    FROM trading.outbox_messages
                    WHERE "PublishedAt" IS NULL
                      AND "SuspendedAt" IS NULL
                      AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {{now}})
                      AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {{now}})
                    ORDER BY "OccurredAt"
                    LIMIT {{batchSize}}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            var leaseExpiresAt = now + leaseDuration;
            foreach (var message in messages)
            {
                message.Claim(leaseId, leaseExpiresAt);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<OutboxMessage>)messages;
        });
    }

    public async Task<bool> MarkPublishedAsync(
        OutboxMessage message,
        Guid leaseId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        message.MarkPublished(leaseId, publishedAt);
        return await SaveLeaseMutationAsync(message, cancellationToken);
    }

    public async Task<bool> RecordFailureAsync(
        OutboxMessage message,
        Guid leaseId,
        string errorCode,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        message.RecordFailure(leaseId, errorCode, error, failedAt, nextAttemptAt);
        return await SaveLeaseMutationAsync(message, cancellationToken);
    }

    public async Task<OutboxReplayResult> ReplayAsync(
        Guid messageId,
        DateTimeOffset replayedAt,
        CancellationToken cancellationToken)
    {
        var message = await context.OutboxMessages.SingleOrDefaultAsync(
            candidate => candidate.MessageId == messageId,
            cancellationToken);
        if (message is null)
        {
            return OutboxReplayResult.NotFound;
        }

        if (message.PublishedAt is not null || message.SuspendedAt is null)
        {
            return OutboxReplayResult.NotSuspended;
        }

        message.Replay(replayedAt);
        await context.SaveChangesAsync(cancellationToken);
        return OutboxReplayResult.Replayed;
    }

    public async Task<TradingReliabilitySnapshot> GetSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAt == null && message.SuspendedAt == null);
        var due = await pending.CountAsync(
            message => (message.NextAttemptAt == null || message.NextAttemptAt <= now)
                && (message.LeaseExpiresAt == null || message.LeaseExpiresAt <= now),
            cancellationToken);
        var delayed = await pending.CountAsync(
            message => message.NextAttemptAt != null && message.NextAttemptAt > now,
            cancellationToken);
        var leased = await pending.CountAsync(
            message => message.LeaseExpiresAt != null && message.LeaseExpiresAt > now,
            cancellationToken);
        var suspended = await context.OutboxMessages.CountAsync(
            message => message.PublishedAt == null && message.SuspendedAt != null,
            cancellationToken);
        var oldestPending = await pending
            .MinAsync(message => (DateTimeOffset?)message.OccurredAt, cancellationToken);
        var publishedRetained = await context.OutboxMessages.CountAsync(
            message => message.PublishedAt != null,
            cancellationToken);
        var inboxRetained = await context.InboxMessages.CountAsync(cancellationToken);

        return new TradingReliabilitySnapshot(
            due,
            delayed,
            leased,
            suspended,
            oldestPending,
            publishedRetained,
            inboxRetained,
            DeadLettersReady: null,
            BrokerReachable: false);
    }

    public async Task<RetentionCleanupResult> CleanupAsync(
        DateTimeOffset publishedOutboxBefore,
        DateTimeOffset inboxBefore,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var outboxDeleted = await context.OutboxMessages
                .Where(message => message.PublishedAt != null && message.PublishedAt < publishedOutboxBefore)
                .ExecuteDeleteAsync(cancellationToken);
            var inboxDeleted = await context.InboxMessages
                .Where(message => message.ProcessedAt < inboxBefore)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RetentionCleanupResult(outboxDeleted, inboxDeleted);
        });
    }

    private async Task<bool> SaveLeaseMutationAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Entry(message).State = EntityState.Detached;
            return false;
        }
    }
}

public enum OutboxReplayResult
{
    Replayed = 0,
    NotFound = 1,
    NotSuspended = 2
}

public sealed record TradingReliabilitySnapshot(
    int OutboxDue,
    int OutboxDelayed,
    int OutboxLeased,
    int OutboxSuspended,
    DateTimeOffset? OldestPendingOccurredAt,
    int PublishedOutboxRetained,
    int InboxRetained,
    uint? DeadLettersReady,
    bool BrokerReachable);

public sealed record RetentionCleanupResult(int PublishedOutboxDeleted, int InboxDeleted);
