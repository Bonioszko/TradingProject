using System.Text.Json;
using PaperTrade.Contracts.Orders;
using PaperTrade.Messaging;

namespace PaperTrade.Api.IntegrationTests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void OrderSubmittedV1EnvelopeRoundTripsWithStableWireNames()
    {
        var messageId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var orderId = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var occurredAt = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var envelope = new MessageEnvelope<OrderSubmittedV1>(
            new MessageMetadata(
                messageId,
                orderId,
                CausationId: null,
                OrderContractNames.SubmittedV1,
                OrderContractNames.Version1,
                occurredAt,
                "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
            new OrderSubmittedV1(
                orderId,
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                OrderSideV1.Buy,
                OrderTypeV1.Market,
                2.5m,
                LimitPrice: null,
                occurredAt));

        var json = MessageJson.Serialize(envelope);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            OrderContractNames.SubmittedV1,
            document.RootElement.GetProperty("metadata").GetProperty("contractType").GetString());
        Assert.Equal(
            OrderContractNames.Version1,
            document.RootElement.GetProperty("metadata").GetProperty("contractVersion").GetInt32());
        Assert.Equal(
            "buy",
            document.RootElement.GetProperty("payload").GetProperty("side").GetString(),
            ignoreCase: true);
        Assert.Equal(
            "market",
            document.RootElement.GetProperty("payload").GetProperty("type").GetString(),
            ignoreCase: true);

        var roundTripped = MessageJson.Deserialize<OrderSubmittedV1>(
            System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(envelope, roundTripped);
    }
}
