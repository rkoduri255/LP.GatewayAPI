using System.Net;

namespace LP.GatewayAPI.Middlewares
{
    public class ApiGatewayMiddleware : IDisposable
    {
        private readonly RequestDelegate _next;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ApiGatewayMiddleware> _logger;
        private readonly List<RouteConfig> _routes;

        public ApiGatewayMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiGatewayMiddleware> logger)
        {
            _next = next;
            _httpClient = new HttpClient();
            _logger = logger;

            // Load routes from appsettings.json
            _routes = configuration.GetSection("Routes").Get<List<RouteConfig>>() ?? new List<RouteConfig>();
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task Invoke(HttpContext context)
        {
            var requestPath = context.Request.Path.Value?.ToLower();
            var method = context.Request.Method.ToUpper();

            // Find matching route
            var route = _routes.FirstOrDefault(r => requestPath != null && requestPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)
                                                    && r.UpstreamHttpMethod.Contains(method));

            if (route == null)
            {
                _logger.LogWarning($"No matching route found for {requestPath}");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.WriteAsync("Route not found.");
                return;
            }

            // Check authentication requirement
            if (route.AuthenticationRequired)
            {
                if (!context.Items.ContainsKey("AccessToken"))
                {
                    _logger.LogWarning($"Unauthorized access to {requestPath}");
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    await context.Response.WriteAsync("Unauthorized access.");
                    return;
                }
            }

            // Construct target URI
            var downstreamHost = route.DownstreamHostAndPorts.FirstOrDefault()?.Host;
            var downstreamPort = route.DownstreamHostAndPorts.FirstOrDefault()?.Port;
            var newRoute = requestPath?.Replace(route.Path, "");

            // Form the target URI with or without a port
            var targetUri = downstreamPort.HasValue
                ? $"{route.DownstreamScheme}://{downstreamHost}:{downstreamPort}{newRoute}{context.Request.QueryString}"
                : $"{route.DownstreamScheme}://{downstreamHost}{newRoute}{context.Request.QueryString}";


            _logger.LogInformation($"Forwarding request to {targetUri}");

            var requestMessage = new HttpRequestMessage(new HttpMethod(method), targetUri);

            // Copy headers from original request
            foreach (var header in context.Request.Headers)
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            // Add Access Token if required
            if (route.AuthenticationRequired && context.Items.ContainsKey("AccessToken"))
            {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.Items["AccessToken"].ToString());
            }

            // Forward request to target URI
            var response = await _httpClient.SendAsync(requestMessage);
            context.Response.StatusCode = (int)response.StatusCode;

            // Copy response headers
            foreach (var header in response.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body);
        }
    }

    // Route configuration model
    public class RouteConfig
    {
        public string Path { get; set; }
        public string UpstreamPathTemplate { get; set; }
        public List<string> UpstreamHttpMethod { get; set; }
        public string DownstreamPathTemplate { get; set; }
        public string DownstreamScheme { get; set; }
        public List<HostAndPort> DownstreamHostAndPorts { get; set; }
        public bool AuthenticationRequired { get; set; }
    }

    public class HostAndPort
    {
        public string Host { get; set; }
        public int? Port { get; set; }
    }
}