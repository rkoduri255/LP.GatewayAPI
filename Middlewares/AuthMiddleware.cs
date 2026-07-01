using LP.GatewayAPI.Utilities;
using Newtonsoft.Json;
using System.Net;

namespace LP.GatewayAPI.Middlewares
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Cryptography _cryptography;
        private readonly ILogger<AuthMiddleware> _logger;

        public AuthMiddleware(RequestDelegate next, Cryptography cryptography, ILogger<AuthMiddleware> logger)
        {
            _next = next;
            _cryptography = cryptography;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var requestHeaders = context.Request.Headers;

            string? eAccessToken = requestHeaders["Authorization"].FirstOrDefault();
            string? lpToken = requestHeaders["lp-auth-token"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(eAccessToken) || !eAccessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Request rejected: missing or malformed Authorization header");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Invalid or missing Authorization token.");
                return;
            }

            if (string.IsNullOrWhiteSpace(lpToken))
            {
                _logger.LogWarning("Request rejected: missing lp-auth-token header");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Missing lp-auth-token.");
                return;
            }

            try
            {
                string dAccessToken = _cryptography.DecryptToken(lpToken);
                dynamic? token = JsonConvert.DeserializeObject(dAccessToken);
                _ = token; // token fields available if needed for future claims enrichment
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Request rejected: lp-auth-token decryption failed");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Invalid lp-auth-token.");
                return;
            }

            string extractedToken = eAccessToken["Bearer ".Length..];
            context.Items["AccessToken"] = extractedToken;
            context.Request.Headers.Remove("api-auth-key");
            context.Request.Headers.Append("api-auth-key", lpToken);

            await _next(context);
        }
    }
}
