using LP.GatewayAPI.Logging;
using System.Net;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAPILogger _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, IAPILogger logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context); // proceed to the next middleware
            }
            catch (Exception ex)
            {
                _logger.Log(ex, ex.Message);

                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var errorResponse = new
                {
                    status = context.Response.StatusCode,
                    message = "Internal Server Error",
                    detail = ex.Message
                };

                var json = JsonSerializer.Serialize(errorResponse);
                await context.Response.WriteAsync(json);
            }
        }
    }

}
