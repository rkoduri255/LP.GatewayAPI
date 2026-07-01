using System.Diagnostics;

namespace LP.GatewayAPI.Middlewares
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var start = Stopwatch.GetTimestamp();
            await _next(context);
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            var correlationId = context.Items["CorrelationId"]?.ToString() ?? "-";
            _logger.LogInformation(
                "[{CorrelationId}] {Method} {Path}{Query} → {StatusCode} ({ElapsedMs:F0}ms)",
                correlationId,
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.Response.StatusCode,
                elapsedMs);
        }
    }
}
