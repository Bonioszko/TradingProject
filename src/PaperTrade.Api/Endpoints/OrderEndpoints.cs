using System.Diagnostics;
using PaperTrade.Api.Orders;

namespace PaperTrade.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapPost("/orders", SubmitOrderAsync);
        group.MapGet("/orders/{orderId:guid}", GetOrderAsync);
        group.MapGet("/accounts/{accountId:guid}", GetAccountAsync);
        group.MapGet("/instruments", GetInstrumentsAsync);

        return endpoints;
    }

    private static async Task<IResult> SubmitOrderAsync(
        SubmitOrderRequest request,
        SynchronousOrderService service,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await service.SubmitAsync(request, cancellationToken);
        return result.Error switch
        {
            OrderSubmissionError.None => Results.Created(
                $"/api/orders/{result.Order!.OrderId}",
                result.Order),
            OrderSubmissionError.NotFound => Results.NotFound(
                new ProblemDetailsResponse("Resource not found", result.ErrorMessage!)),
            OrderSubmissionError.Unsupported => Results.UnprocessableEntity(
                new ProblemDetailsResponse("Unsupported order", result.ErrorMessage!)),
            _ => throw new UnreachableException()
        };
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId,
        SynchronousOrderService service,
        CancellationToken cancellationToken)
    {
        var order = await service.FindOrderAsync(orderId, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(order);
    }

    private static async Task<IResult> GetAccountAsync(
        Guid accountId,
        SynchronousOrderService service,
        CancellationToken cancellationToken)
    {
        var account = await service.FindAccountAsync(accountId, cancellationToken);
        return account is null ? Results.NotFound() : Results.Ok(account);
    }

    private static async Task<IResult> GetInstrumentsAsync(
        SynchronousOrderService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListInstrumentsAsync(cancellationToken));

    private static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.AccountId == Guid.Empty)
        {
            errors[nameof(request.AccountId)] = ["An account ID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            errors[nameof(request.Symbol)] = ["A symbol is required."];
        }

        if (request.Quantity <= 0)
        {
            errors[nameof(request.Quantity)] = ["Quantity must be greater than zero."];
        }

        if (!Enum.IsDefined(request.Side))
        {
            errors[nameof(request.Side)] = ["A valid order side is required."];
        }

        if (!Enum.IsDefined(request.Type))
        {
            errors[nameof(request.Type)] = ["A valid order type is required."];
        }

        return errors;
    }

    private sealed record ProblemDetailsResponse(string Title, string Detail);
}
