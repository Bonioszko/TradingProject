using PaperTrade.Api.Messaging;
using PaperTrade.Messaging;

namespace PaperTrade.Api.Endpoints;

public static class ReliabilityEndpoints
{
    public static IEndpointRouteBuilder MapReliabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/operations/reliability")
            .WithTags("Reliability operations");

        group.MapGet("", async (
            TradingReliabilityOperations operations,
            CancellationToken cancellationToken) =>
            Results.Ok(await operations.InspectAsync(cancellationToken)));

        group.MapPost("/outbox/{messageId:guid}/replay", async (
            Guid messageId,
            TradingReliabilityOperations operations,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.ReplayOutboxAsync(messageId, cancellationToken);
            return result switch
            {
                OutboxReplayResult.Replayed => Results.NoContent(),
                OutboxReplayResult.NotFound => Results.NotFound(),
                OutboxReplayResult.NotSuspended => Results.Conflict(
                    new { detail = "Only an unpublished suspended outbox message can be replayed." }),
                _ => throw new InvalidOperationException($"Unknown outbox replay result '{result}'.")
            };
        });

        group.MapPost("/dead-letters/replay-next", async (
            TradingReliabilityOperations operations,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.ReplayNextDeadLetterAsync(cancellationToken);
            return result switch
            {
                DeadLetterReplayResult.Replayed => Results.NoContent(),
                DeadLetterReplayResult.Empty => Results.NotFound(),
                DeadLetterReplayResult.InvalidEnvelope => Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Dead letter cannot be replayed",
                    detail: "The next dead letter is not a valid supported order-submitted envelope."),
                _ => throw new InvalidOperationException($"Unknown dead-letter replay result '{result}'.")
            };
        });

        group.MapPost("/retention/run", async (
            TradingReliabilityOperations operations,
            CancellationToken cancellationToken) =>
            Results.Ok(await operations.RunRetentionCleanupAsync(cancellationToken)));

        return endpoints;
    }
}
