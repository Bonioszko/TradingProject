using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace PaperTrade.Api.Messaging;

public sealed class TradingOutboxHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<TradingReliabilityOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<TradingOutboxStore>();
            var now = timeProvider.GetUtcNow();
            var snapshot = await store.GetSnapshotAsync(now, cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["outboxDue"] = snapshot.OutboxDue,
                ["outboxDelayed"] = snapshot.OutboxDelayed,
                ["outboxLeased"] = snapshot.OutboxLeased,
                ["outboxSuspended"] = snapshot.OutboxSuspended
            };

            if (snapshot.OutboxSuspended > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{snapshot.OutboxSuspended} outbox message(s) require operator replay.",
                    data: data);
            }

            if (snapshot.OldestPendingOccurredAt is { } oldest
                && now - oldest > options.Value.StaleOutboxWarning)
            {
                return HealthCheckResult.Degraded(
                    $"The oldest unpublished outbox message is older than {options.Value.StaleOutboxWarning}.",
                    data: data);
            }

            return HealthCheckResult.Healthy("The trading outbox is current.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "The trading outbox could not be inspected.",
                exception);
        }
    }
}
