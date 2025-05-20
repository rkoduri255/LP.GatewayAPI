using Microsoft.AspNetCore.Http;
using System;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class ApiGatewayMiddleware : IDisposable
    {
        private readonly RequestDelegate _next;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ApiGatewayMiddleware> _logger;
        private readonly List<RouteConfig> _routes;
        private readonly List<RouteConfig> _defaultRoutes;
        private readonly List<VersionedRoutes> _versionedRoutes;        

        public ApiGatewayMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<ApiGatewayMiddleware> logger, IHttpClientFactory httpClientFactory)
        {
            _next = next;
            _httpClient = httpClientFactory.CreateClient("HttpClientWithSSLUntrusted");
            _logger = logger;

            var routesFilePath = Path.Combine(AppContext.BaseDirectory, "routes.json");
            if (File.Exists(routesFilePath))
            {
                var json = File.ReadAllText(routesFilePath);
                var routesRoot = JsonSerializer.Deserialize<VersionedRoutesRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                // Simplify collection initialization for _versionedRoutes and _defaultRoutes
                _versionedRoutes = routesRoot?.Versions ?? new List<VersionedRoutes>();
                _defaultRoutes = routesRoot?.DefaultRoutes ?? new List<RouteConfig>();
            }
            else
            {
                _versionedRoutes = new List<VersionedRoutes>();
                _defaultRoutes = new List<RouteConfig>();
                _logger.LogWarning("routes.json not found. No routes loaded.");
            }

        }


        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task Invoke(HttpContext context)
        {
            var requestPath = context.Request.Path.Value?.ToLower();           

            // Read version from header
            var version = context.Request.Headers["version"].FirstOrDefault();

            RouteConfig route = null;

            if (!string.IsNullOrEmpty(version))
            {
                var versionGroup = _versionedRoutes.FirstOrDefault(v =>
                    v.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                );
                if (versionGroup != null)
                {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                    route = versionGroup.Routes.FirstOrDefault(r =>
                        requestPath != null &&
                        requestPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)                        
                    );
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                }
            }

            // If no version or no matching versioned route, use default routes
            if (route == null)
            {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                route = _defaultRoutes.FirstOrDefault(r =>
                    requestPath != null &&
                    requestPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)                   
                );
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            }

            if (route == null)
            {
                _logger.LogWarning($"No matching route found for {requestPath} with version {version}");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.WriteAsync("Route not found.");
                return;
            }            
           
            var newRoute = requestPath?.Replace(route.Path, "");

            // Form the target URI with or without a port
            var targetUri = $"{route.ApiUri}{newRoute}{context.Request.QueryString}";

            _logger.LogInformation($"Forwarding request to {targetUri}");

            var method = context.Request.Method.ToUpper();

            // Handle payload for POST, PUT, PATCH
            string bodyContent = null;
            if (method == "POST" || method == "PUT" || method == "PATCH")
            {
                context.Request.EnableBuffering();
                using (var reader = new StreamReader(
                    context.Request.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true))
                {
                    bodyContent = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                }
            }

            HttpRequestMessage requestMessage = CreateRequestObject(context.Request, new Uri(targetUri), bodyContent);                      

            // Forward request to target URI
            var response = await _httpClient.SendAsync(requestMessage);
            context.Response.StatusCode = (int)response.StatusCode;           
            await response.Content.CopyToAsync(context.Response.Body);
        }

        private HttpRequestMessage CreateRequestObject(HttpRequest httpRequest, Uri uri, string content = null)
        {
            HttpRequestMessage request;
            if (content != null)
                request = RequestTranscriptHelpers.ToHttpRequestMessage(httpRequest, uri, content);
            else
                request = RequestTranscriptHelpers.ToHttpRequestMessage(httpRequest, uri);

            // In case we are forwarding the request to a different host, replace the host header with our destination.
            request.Headers.Remove("Host");
            request.Headers.Add("Host", uri.Host);
            request.Headers.Remove("Origin");
            return request;
        }

    }

    // Route configuration model
    public class RouteConfig
    {
        public string Path { get; set; }             
        public string ApiUri { get; set; }                             
    }

    public class HostAndPort
    {
        public string Host { get; set; }
        public int? Port { get; set; }
    }

    public class VersionedRoutesRoot
    {
        public List<RouteConfig> DefaultRoutes { get; set; }
        public List<VersionedRoutes> Versions { get; set; }
    }


    public class VersionedRoutes
    {
        public string Version { get; set; }
        public List<RouteConfig> Routes { get; set; }
    }

    public static class RequestTranscriptHelpers
    {
        public static HttpRequestMessage ToHttpRequestMessage(this HttpRequest req, Uri path)
            => new HttpRequestMessage()
                .SetMethod(req)
                .SetAbsoluteUri(path)
                .SetHeaders(req)
                .SetContent(req)
                .SetContentType(req);
        public static HttpRequestMessage ToHttpRequestMessage(this HttpRequest req, Uri path, string content)
            => new HttpRequestMessage()
                .SetMethod(req)
                .SetAbsoluteUri(path)
                .SetHeaders(req)
                .SetContent(content);

        private static HttpRequestMessage SetAbsoluteUri(this HttpRequestMessage msg, Uri path)
            => msg.Set(m => m.RequestUri = path);

        private static HttpRequestMessage SetMethod(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Method = new HttpMethod(req.Method));

        private static HttpRequestMessage SetHeaders(this HttpRequestMessage msg, HttpRequest req)
            => req.Headers.Aggregate(msg, (acc, h) => acc.Set(m => m.Headers.TryAddWithoutValidation(h.Key, h.Value.AsEnumerable())));

        private static HttpRequestMessage SetContent(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Content = new StreamContent(req.Body));

        private static HttpRequestMessage SetContent(this HttpRequestMessage msg, String req)
            => msg.Set(m => m.Content = new StringContent(req, Encoding.UTF8, "application/json"));

        private static HttpRequestMessage SetContentType(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Content.Headers.Add("Content-Type", req.ContentType), applyIf: req.Headers.ContainsKey("Content-Type"));

        private static HttpRequestMessage Set(this HttpRequestMessage msg, Action<HttpRequestMessage> config, bool applyIf = true)
        {
            if (applyIf)
            {
                config.Invoke(msg);
            }

            return msg;
        }
    }

}