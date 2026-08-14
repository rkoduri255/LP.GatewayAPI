using LP.GatewayAPI.Logging;
using System.Net;

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
        private readonly RouteVersionResolver _routeVersionResolver;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IAPILogger _apiLogger;
        private readonly ILogger<ApiGatewayMiddleware> _logger;

        public ApiGatewayMiddleware(
            RequestDelegate next,
            RouteVersionResolver routeVersionResolver,
            IHttpClientFactory httpClientFactory,
            IAPILogger apiLogger,
            ILogger<ApiGatewayMiddleware> logger)
        {
            _next = next;
            _routeVersionResolver = routeVersionResolver;
            _httpClientFactory = httpClientFactory;
            _apiLogger = apiLogger;
            _logger = logger;
        }

        // Public-facing requests arrive as /api/gateway/{serviceName}/{rest}, e.g. Angular calling
        // https://lp-qa-in-2.zeqo.com/api/gateway/lpservicesaddress/getstudentperformance. This
        // prefix is stripped before the service-key extraction below; if a reverse proxy in front
        // of this app already strips it, the StartsWith check below is simply a no-op.
        private const string GatewayPathPrefix = "api/lp.gateway.api";

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? "-";
            var requestPath = context.Request.Path.Value?.ToLower() ?? string.Empty;

            if (requestPath.StartsWith(GatewayPathPrefix, StringComparison.OrdinalIgnoreCase))
                requestPath = requestPath[GatewayPathPrefix.Length..];

            // The first remaining path segment IS the service key, e.g.
            // "/lpservicesaddress/some/downstream/path" -> serviceKey "lpservicesaddress",
            // remainingPath "/some/downstream/path". There is no separate routes.json anymore --
            // route-versions.json is the only source of what services exist and where they go.
            var trimmed = requestPath.Trim('/');
            var splitIndex = trimmed.IndexOf('/');
            var serviceKey = splitIndex < 0 ? trimmed : trimmed[..splitIndex];
            var remainingPath = splitIndex < 0 ? string.Empty : trimmed[splitIndex..];

            var clientVersion = context.Request.Headers[RouteVersionResolver.ClientVersionHeader].FirstOrDefault();
            var resolved = _routeVersionResolver.Resolve(clientVersion, serviceKey);

            if (resolved == null)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] No route for {Path} (service '{ServiceKey}', clientVersion '{ClientVersion}')",
                    correlationId, requestPath, serviceKey, clientVersion ?? "-");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.WriteAsync("Route not found.");
                return;
            }

            var targetUri = string.IsNullOrWhiteSpace(resolved.Value.Version)
                ? $"{resolved.Value.ApiUri}{remainingPath}{context.Request.QueryString}"
                : $"{resolved.Value.ApiUri}/{resolved.Value.Version}{remainingPath}{context.Request.QueryString}";

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
    }
}
