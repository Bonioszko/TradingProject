using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace PaperTrade.Api.ErrorHandling;

internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = MapException(exception);
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (error.StatusCode == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path.ToString(),
                traceId,
                exception);
        }
        else
        {
            LogHandledException(
                logger,
                exception.GetType().Name,
                error.StatusCode,
                httpContext.Request.Method,
                httpContext.Request.Path.ToString(),
                traceId);
        }

        await Results.Problem(
                statusCode: error.StatusCode,
                title: error.Title,
                detail: error.Detail,
                instance: httpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = traceId
                })
            .ExecuteAsync(httpContext);

        return true;
    }

    private static ErrorResponse MapException(Exception exception) =>
        exception switch
        {
            DbUpdateConcurrencyException => new(
                StatusCodes.Status409Conflict,
                "Concurrent account update",
                "The account changed while the order was executing. Submit the order again."),
            ArgumentException => new(
                StatusCodes.Status400BadRequest,
                "Invalid order",
                exception.Message),
            InvalidOperationException => new(
                StatusCodes.Status422UnprocessableEntity,
                "Order rejected",
                exception.Message),
            _ => new(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "The request could not be completed. Use the trace ID when reporting this problem.")
        };

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path}. Trace ID: {TraceId}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        string method,
        string path,
        string traceId,
        Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Handled {ExceptionType} as HTTP {StatusCode} for {Method} {Path}. Trace ID: {TraceId}")]
    private static partial void LogHandledException(
        ILogger logger,
        string exceptionType,
        int statusCode,
        string method,
        string path,
        string traceId);

    private sealed record ErrorResponse(int StatusCode, string Title, string Detail);
}
