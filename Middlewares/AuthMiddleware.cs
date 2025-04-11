using System.Net;
using LP.GatewayAPI.Utilities;
using Newtonsoft.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthMiddleware> _logger;

        public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var requestHeaders = context.Request.Headers;

            // Extract authentication tokens
            string eAccessToken = requestHeaders["Authorization"].FirstOrDefault();
            string lpToken = requestHeaders["lp-auth-token"].FirstOrDefault();

            // Validate Authorization header
            if (string.IsNullOrWhiteSpace(eAccessToken) || !eAccessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Invalid or missing Authorization token.");
                return;
            }

            if (string.IsNullOrWhiteSpace(lpToken))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Missing lp-auth-token.");
                return;
            }

            try
            {
                // Decrypt the lp-auth-token
                Cryptography cryptography = new Cryptography();
                string dAccessToken = cryptography.DecryptToken(lpToken);
                dynamic token = JsonConvert.DeserializeObject(dAccessToken);

                // Set the Access Token in headers (Stripping 'Bearer ' prefix)
                string extractedToken = eAccessToken.Substring("Bearer ".Length);
                context.Items["AccessToken"] = extractedToken;
                context.Request.Headers.Remove("api-auth-key");
                context.Request.Headers.Add("api-auth-key", lpToken);

                // Proceed with the request
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed.");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsync("Authentication failed.");
            }
        }
    }
}