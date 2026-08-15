using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperTrade.Api.Orders;
using PaperTrade.Domain.Orders;
using PaperTrade.Trading.Persistence;

namespace PaperTrade.Api.IntegrationTests;

public sealed class OrderApiTests(PaperTradeApiFixture fixture) : IClassFixture<PaperTradeApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task SubmitMarketBuyPersistsFillAndRejectsUnaffordableOrder()
    {
        using var filledResponse = await fixture.Client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                accountId = SeedData.AccountId,
                symbol = "MSFT",
                side = "buy",
                type = "market",
                quantity = 5m
            });

        Assert.Equal(HttpStatusCode.Created, filledResponse.StatusCode);
        Assert.NotNull(filledResponse.Headers.Location);

        var filledOrder = await filledResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(filledOrder);
        Assert.Equal(OrderStatus.Filled, filledOrder.Status);
        Assert.Equal(400m, filledOrder.Execution?.Price);
        Assert.Equal(5m, filledOrder.Execution?.Quantity);

        var persistedOrder = await fixture.Client.GetFromJsonAsync<OrderResponse>(
            filledResponse.Headers.Location,
            JsonOptions);
        Assert.NotNull(persistedOrder);
        Assert.Equal(filledOrder.OrderId, persistedOrder.OrderId);
        Assert.Equal(filledOrder.Status, persistedOrder.Status);
        Assert.Equal(filledOrder.Execution?.ExecutionId, persistedOrder.Execution?.ExecutionId);
        Assert.Equal(filledOrder.Execution?.Price, persistedOrder.Execution?.Price);

        var accountAfterFill = await fixture.Client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{SeedData.AccountId}",
            JsonOptions);
        Assert.NotNull(accountAfterFill);
        Assert.Equal(98_000m, accountAfterFill.AvailableCash);

        using var rejectedResponse = await fixture.Client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                accountId = SeedData.AccountId,
                symbol = "MSFT",
                side = "buy",
                type = "market",
                quantity = 1_000m
            });

        Assert.Equal(HttpStatusCode.Created, rejectedResponse.StatusCode);
        var rejectedOrder = await rejectedResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(rejectedOrder);
        Assert.Equal(OrderStatus.Rejected, rejectedOrder.Status);
        Assert.Null(rejectedOrder.Execution);
        Assert.NotNull(rejectedOrder.RejectionReason);

        var accountAfterRejection = await fixture.Client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{SeedData.AccountId}",
            JsonOptions);
        Assert.Equal(98_000m, accountAfterRejection?.AvailableCash);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
