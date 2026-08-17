using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PaperTrade.Api.Orders;
using PaperTrade.Contracts.Orders;
using PaperTrade.Domain.Orders;

namespace PaperTrade.Api.IntegrationTests;

public sealed class OrderApiTests(PaperTradeApiFixture fixture) : IClassFixture<PaperTradeApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task SubmissionReturnsPendingThenWorkerFillsOrRejects()
    {
        var accountId = await fixture.CreateAccountAsync(2_500m);

        using var filledResponse = await SubmitAsync(accountId, quantity: 5m, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Accepted, filledResponse.StatusCode);
        Assert.NotNull(filledResponse.Headers.Location);
        var acceptedOrder = await filledResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(acceptedOrder);
        Assert.Equal(OrderStatus.Pending, acceptedOrder.Status);
        Assert.Null(acceptedOrder.Execution);

        var filledOrder = await WaitForTerminalOrderAsync(acceptedOrder.OrderId);
        Assert.Equal(OrderStatus.Filled, filledOrder.Status);
        Assert.Equal(400m, filledOrder.Execution?.Price);
        Assert.Equal(5m, filledOrder.Execution?.Quantity);

        using var rejectedResponse = await SubmitAsync(accountId, quantity: 2m, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Accepted, rejectedResponse.StatusCode);
        var rejectedSubmission = await rejectedResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(rejectedSubmission);
        var rejectedOrder = await WaitForTerminalOrderAsync(rejectedSubmission.OrderId);
        Assert.Equal(OrderStatus.Rejected, rejectedOrder.Status);
        Assert.Null(rejectedOrder.Execution);
        Assert.NotNull(rejectedOrder.RejectionReason);

        var account = await fixture.Client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountId}",
            JsonOptions);
        Assert.Equal(500m, account?.AvailableCash);
    }

    [Fact]
    public async Task IdempotencyKeyCreatesOnlyOneOrderAndCannotBeReusedForDifferentRequest()
    {
        var accountId = await fixture.CreateAccountAsync(1_000m);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var firstTask = SubmitAsync(accountId, quantity: 1m, idempotencyKey);
        var retryTask = SubmitAsync(accountId, quantity: 1m, idempotencyKey);
        using var first = await firstTask;
        using var retry = await retryTask;

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        var firstOrder = await first.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        var retriedOrder = await retry.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(firstOrder);
        Assert.NotNull(retriedOrder);
        Assert.Equal(firstOrder.OrderId, retriedOrder.OrderId);
        await WaitForTerminalOrderAsync(firstOrder.OrderId);

        using var conflict = await SubmitAsync(accountId, quantity: 2m, idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var effects = await fixture.QueryAsync(async context => new
        {
            Orders = await context.Orders.CountAsync(order => order.AccountId == accountId),
            Executions = await context.Executions.CountAsync(execution =>
                context.Orders
                    .Where(order => order.AccountId == accountId)
                    .Select(order => order.Id)
                    .Contains(execution.OrderId)),
            SubmittedMessages = await context.OutboxMessages.CountAsync(message =>
                message.AggregateId == firstOrder.OrderId
                && message.ContractType == OrderContractNames.SubmittedV1)
        });
        Assert.Equal(1, effects.Orders);
        Assert.Equal(1, effects.Executions);
        Assert.Equal(1, effects.SubmittedMessages);
    }

    [Fact]
    public async Task DuplicateDeliveryDoesNotRepeatExecutionOrCashMovement()
    {
        var accountId = await fixture.CreateAccountAsync(1_000m);
        using var response = await SubmitAsync(accountId, quantity: 1m, Guid.NewGuid().ToString("N"));
        var submission = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(submission);
        await WaitForTerminalOrderAsync(submission.OrderId);

        await fixture.PublishSubmittedMessageAgainAsync(submission.OrderId);
        await fixture.WaitForSubmittedQueueToDrainAsync(TimeSpan.FromSeconds(10));

        var effects = await fixture.QueryAsync(async context => new
        {
            Executions = await context.Executions.CountAsync(execution => execution.OrderId == submission.OrderId),
            Results = await context.OutboxMessages.CountAsync(message =>
                message.AggregateId == submission.OrderId
                && message.ContractType == OrderContractNames.ResultV1),
            Cash = await context.Accounts
                .Where(account => account.Id == accountId)
                .Select(account => account.AvailableCash)
                .SingleAsync()
        });
        Assert.Equal(1, effects.Executions);
        Assert.Equal(1, effects.Results);
        Assert.Equal(600m, effects.Cash);
    }

    [Fact]
    public async Task ConcurrentOrdersCannotOverspendAccount()
    {
        var accountId = await fixture.CreateAccountAsync(500m);
        var firstTask = SubmitAsync(accountId, quantity: 1m, Guid.NewGuid().ToString("N"));
        var secondTask = SubmitAsync(accountId, quantity: 1m, Guid.NewGuid().ToString("N"));
        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;
        var first = await firstResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        var second = await secondResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var terminal = await Task.WhenAll(
            WaitForTerminalOrderAsync(first.OrderId),
            WaitForTerminalOrderAsync(second.OrderId));
        Assert.Single(terminal, order => order.Status == OrderStatus.Filled);
        Assert.Single(terminal, order => order.Status == OrderStatus.Rejected);

        var account = await fixture.Client.GetFromJsonAsync<AccountResponse>(
            $"/api/accounts/{accountId}",
            JsonOptions);
        Assert.Equal(100m, account?.AvailableCash);
    }

    [Fact]
    public async Task AcceptedOrderWaitsDurablyWhileWorkerIsStoppedAndCompletesAfterRestart()
    {
        var accountId = await fixture.CreateAccountAsync(1_000m);
        await fixture.StopWorkerAsync();

        try
        {
            using var response = await SubmitAsync(accountId, quantity: 1m, Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var submission = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
            Assert.NotNull(submission);

            var pending = await fixture.Client.GetFromJsonAsync<OrderResponse>(
                $"/api/orders/{submission.OrderId}",
                JsonOptions);
            Assert.Equal(OrderStatus.Pending, pending?.Status);

            await fixture.StartWorkerAsync();
            var completed = await WaitForTerminalOrderAsync(submission.OrderId);
            Assert.Equal(OrderStatus.Filled, completed.Status);
        }
        finally
        {
            await fixture.StartWorkerAsync();
        }
    }

    [Fact]
    public async Task PoisonOrderMessageIsDeadLettered()
    {
        await fixture.PublishPoisonSubmittedMessageAsync();
        await fixture.WaitForDeadLetterAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SubmitInvalidMarketOrderReturnsProblemDetailsFromGlobalHandler()
    {
        var accountId = await fixture.CreateAccountAsync(1_000m);
        using var response = await SubmitAsync(
            accountId,
            quantity: 1m,
            Guid.NewGuid().ToString("N"),
            limitPrice: 400m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = problem.RootElement;
        Assert.Equal("Invalid order", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Contains(
            "A market order cannot specify a limit price.",
            root.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    private async Task<HttpResponseMessage> SubmitAsync(
        Guid accountId,
        decimal quantity,
        string idempotencyKey,
        decimal? limitPrice = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                symbol = "MSFT",
                side = "buy",
                type = "market",
                quantity,
                limitPrice
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await fixture.Client.SendAsync(request);
    }

    private async Task<OrderResponse> WaitForTerminalOrderAsync(Guid orderId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        OrderResponse? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await fixture.Client.GetAsync($"/api/orders/{orderId}");
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Polling order '{orderId}' returned {(int)response.StatusCode}: " +
                    await response.Content.ReadAsStringAsync());
            }

            latest = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
            if (latest?.Status is OrderStatus.Filled or OrderStatus.Rejected)
            {
                return latest;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"Order '{orderId}' did not become terminal. Last status: '{latest?.Status}'.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
