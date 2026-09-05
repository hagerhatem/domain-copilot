using Serilog.Context;

namespace DomainCopilot.Api.Correlation;

/// <summary>
/// Prompt 11.2: generates/propagates a correlation id for every HTTP request.
/// Reads X-Correlation-Id from the incoming request if the caller already supplied
/// one (lets an upstream gateway/client thread its own id through); otherwise
/// generates a new Guid. Echoes it back on the response header, stores it on
/// HttpContext.Items for controllers to read (see RunsController/AdminController),
/// and pushes it into Serilog's ambient LogContext so every log statement written
/// anywhere during this request - middleware, controller, PipelineOrchestrator,
/// each agent, each LLM call - carries it automatically, with no need to thread a
/// correlation id parameter through every method signature by hand.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string HttpContextItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Items[HttpContextItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public static class HttpContextCorrelationIdExtensions
{
    /// <summary>Reads the id CorrelationIdMiddleware attached to this request. Falls back to TraceIdentifier only if the middleware somehow didn't run (defensive, should not normally happen).</summary>
    public static string GetCorrelationId(this HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value) && value is string id
            ? id
            : context.TraceIdentifier;
}