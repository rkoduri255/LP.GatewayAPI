using System.Net;
using LP.GatewayAPI.Utilities;
using Newtonsoft.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;       

        public AuthMiddleware(RequestDelegate next)
        {
            _next = next;           
        }

        public async Task Invoke(HttpContext context)
        {
            var requestHeaders = context.Request.Headers;

            // Extract authentication tokens
            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string eAccessToken = requestHeaders["Authorization"].FirstOrDefault();
            #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string lpToken = requestHeaders["lp-auth-token"].FirstOrDefault();
            #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

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


            // Decrypt the lp-auth-token
            Cryptography cryptography = new Cryptography();
            string dAccessToken = cryptography.DecryptToken(lpToken);
            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            dynamic token = JsonConvert.DeserializeObject(dAccessToken);
            #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            // Set the Access Token in headers (Stripping 'Bearer ' prefix)
            string extractedToken = eAccessToken.Substring("Bearer ".Length);
            context.Items["AccessToken"] = extractedToken;
            context.Request.Headers.Remove("api-auth-key");
            #pragma warning disable ASP0019 // Suggest using IHeaderDictionary.Append or the indexer
            context.Request.Headers.Add("api-auth-key", lpToken);
            #pragma warning restore ASP0019 // Suggest using IHeaderDictionary.Append or the indexer

            // Proceed with the request
            await _next(context);
        }
    }
}