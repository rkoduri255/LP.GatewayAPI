using LP.GatewayAPI.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class ApiGatewayMiddleware
    {
        // RFC 7230 hop-by-hop headers that must not be forwarded
        private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
            "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Content-Length"
        };

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<RoutesRoot> _routesMonitor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAPILogger _apiLogger;
        private readonly ILogger<ApiGatewayMiddleware> _logger;
        private List<RouteConfig> _sortedRoutes = [];

        public ApiGatewayMiddleware(
            RequestDelegate next,
            IOptionsMonitor<RoutesRoot> routesMonitor,
            IHttpClientFactory httpClientFactory,
            IAPILogger apiLogger,
            ILogger<ApiGatewayMiddleware> logger)
        {
            _next = next;
            _routesMonitor = routesMonitor;
            _httpClientFactory = httpClientFactory;
            _apiLogger = apiLogger;
            _logger = logger;

            ApplyRoutes(routesMonitor.CurrentValue);
            routesMonitor.OnChange(ApplyRoutes);
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? "-";
            var requestPath = context.Request.Path.Value?.ToLower();

            var route = _sortedRoutes.FirstOrDefault(r =>
                requestPath?.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase) == true);

            if (route == null)
            {
                _logger.LogWarning("[{CorrelationId}] No route for {Path}", correlationId, requestPath);
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.WriteAsync("Route not found.");
                return;
            }

            var remainingPath = requestPath?[route.Path.Length..] ?? string.Empty;
            var targetUri = $"{route.ApiUri}/{route.Version}{remainingPath}{context.Request.QueryString}";
            var method = context.Request.Method.ToUpper();

            HttpContent? httpContent = null;
            if (method is "POST" or "PUT" or "PATCH")
            {
                context.Request.EnableBuffering();
                var ms = new MemoryStream();
                await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
                context.Request.Body.Position = 0;
                ms.Position = 0;
                httpContent = new StreamContent(ms);
                if (!string.IsNullOrEmpty(context.Request.ContentType))
                    httpContent.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
            }

            var requestMessage = BuildForwardRequest(context.Request, new Uri(targetUri), httpContent);

            try
            {
                var client = _httpClientFactory.CreateClient("HttpClientWithSSLUntrusted");
                var response = await client.SendAsync(requestMessage, context.RequestAborted);

                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var header in response.Headers)
                    context.Response.Headers.TryAdd(header.Key, header.Value.ToArray());
                foreach (var header in response.Content.Headers)
                    context.Response.Headers.TryAdd(header.Key, header.Value.ToArray());

                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);

                if ((int)response.StatusCode >= 500)
                {
                    var downstreamEx = new Exception($"Downstream {(int)response.StatusCode} from {targetUri}");
                    await _apiLogger.LogAsync(downstreamEx, "Downstream service returned 5xx error.");
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation("[{CorrelationId}] Request cancelled by client", correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{CorrelationId}] Error forwarding to {TargetUri}", correlationId, targetUri);
                await _apiLogger.LogAsync(ex, "Error forwarding request to downstream service.");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await context.Response.WriteAsync("Error forwarding request.");
            }
        }

        private static HttpRequestMessage BuildForwardRequest(HttpRequest httpRequest, Uri uri, HttpContent? content)
        {
            var message = new HttpRequestMessage(new HttpMethod(httpRequest.Method), uri);

            foreach (var header in httpRequest.Headers)
            {
                if (!HopByHopHeaders.Contains(header.Key))
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
            }

            message.Headers.Host = uri.Host;

            if (content != null)
                message.Content = content;

            return message;
        }

        private void ApplyRoutes(RoutesRoot routes)
        {
            _sortedRoutes = (routes.Routes ?? [])
                .OrderByDescending(r => r.Path.Length)
                .ToList();

            ValidateRoutes(routes);
        }

        private void ValidateRoutes(RoutesRoot routes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in routes.Routes ?? [])
            {
                if (!seen.Add(r.Path))
                    _logger.LogWarning("Ambiguous route detected: '{Path}' is defined more than once", r.Path);
            }
        }
    }

    public class RouteConfig
    {
        public string Path { get; set; } = string.Empty;
        public string ApiUri { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    public class RoutesRoot
    {
        public List<RouteConfig> Routes { get; set; } = [];
    }
}
