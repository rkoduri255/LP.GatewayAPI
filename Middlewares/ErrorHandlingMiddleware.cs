using LP.GatewayAPI.Logging;
using System.Net;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAPILogger _apiLogger;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, IAPILogger apiLogger, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _apiLogger = apiLogger;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var correlationId = context.Items["CorrelationId"]?.ToString() ?? "-";
                _logger.LogError(ex, "[{CorrelationId}] Unhandled exception", correlationId);
                await _apiLogger.LogAsync(ex, ex.Message);

                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var errorResponse = new { status = 500, message = "Internal Server Error", correlationId };
                await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
            }
        }
    }
}
