using LP.GatewayAPI.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    public class ApiGatewayMiddleware : IDisposable
    {        
        private readonly HttpClient _httpClient;               
        private readonly List<RouteConfig> _defaultRoutes;
        private readonly List<VersionedRoutes> _versionedRoutes;
        private readonly IAPILogger _logger;
        private readonly RequestDelegate _next;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiGatewayMiddleware"/> class.
        /// Loads route configuration from routes.json and sets up the HTTP client.
        /// </summary>
        public ApiGatewayMiddleware(RequestDelegate next, IAPILogger logger, IHttpClientFactory httpClientFactory)
        {
            _next = next;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("HttpClientWithSSLUntrusted");           

            var routesFilePath = Path.Combine(AppContext.BaseDirectory, "routes.json");
            if (File.Exists(routesFilePath))
            {
                var json = File.ReadAllText(routesFilePath);
                var routesRoot = JsonSerializer.Deserialize<VersionedRoutesRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                // Simplify collection initialization for _versionedRoutes and _defaultRoutes
                _versionedRoutes = routesRoot?.Versions ?? [];
                _defaultRoutes = routesRoot?.DefaultRoutes ?? [];
            }
            else
            {
                _versionedRoutes = [];
                _defaultRoutes = [];               
            }

        }


        /// <summary>
        /// Disposes the internal HttpClient instance used for forwarding requests.
        /// </summary>
        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }        

        /// <summary>
        /// Main middleware entry point. Matches the incoming request to a configured route,
        /// constructs the downstream target URI, forwards the request (including payload and headers),
        /// and copies the downstream response back to the client.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        public async Task Invoke(HttpContext context)
        {
            var requestPath = context.Request.Path.Value?.ToLower();           

            // Read version from header
            var version = context.Request.Headers["version"].FirstOrDefault();

            RouteConfig? route = null;

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
            #pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            route ??= _defaultRoutes.FirstOrDefault(r =>
                    requestPath != null &&
                    requestPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)                   
                );
            #pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            if (route == null)
            {
                _logger.Log(new Exception($"No matching route found for {requestPath} with version {version}"), $"No matching route found for {requestPath} with version {version}");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await context.Response.WriteAsync("Route not found.");
                return;
            }            
           
            var newRoute = requestPath?.Replace(route.Path, "");

            // Form the target URI with or without a port
            var targetUri = $"{route.ApiUri}{newRoute}{context.Request.QueryString}";           

            var method = context.Request.Method.ToUpper();

            // Handle payload for POST, PUT, PATCH           
            HttpContent? httpContent = null;
            if (method == "POST" || method == "PUT" || method == "PATCH")
            {
                context.Request.EnableBuffering();
                var memoryStream = new MemoryStream();
                await context.Request.Body.CopyToAsync(memoryStream);
                context.Request.Body.Position = 0;
                memoryStream.Position = 0;
                httpContent = new StreamContent(memoryStream);
                if (!string.IsNullOrEmpty(context.Request.ContentType))
                    httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(context.Request.ContentType);
            }


            HttpRequestMessage requestMessage = CreateRequestObject(context.Request, new Uri(targetUri), httpContent);

            try
            {
                var response = await _httpClient.SendAsync(requestMessage);
                context.Response.StatusCode = (int)response.StatusCode;
                await response.Content.CopyToAsync(context.Response.Body);

                if ((int)response.StatusCode >= 500)
                {
                    _logger.Log(new Exception($"Downstream service error: {(int)response.StatusCode}"), "Downstream service returned 5xx error.");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(ex, "Error forwarding request to downstream service.");
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                await context.Response.WriteAsync("Error forwarding request.");
            }

        }

        /// <summary>
        /// Creates an <see cref="HttpRequestMessage"/> for forwarding to the downstream service.
        /// Copies headers and sets the request content if provided.
        /// </summary>
        /// <param name="httpRequest">The incoming HTTP request.</param>
        /// <param name="uri">The downstream target URI.</param>
        /// <param name="content">The request body content, if any.</param>
        /// <returns>A configured <see cref="HttpRequestMessage"/> for forwarding.</returns>
        private static HttpRequestMessage CreateRequestObject(HttpRequest httpRequest, Uri uri, HttpContent? content = null)
        {
            HttpRequestMessage request;            
            request = RequestTranscriptHelpers.ToHttpRequestMessage(httpRequest, uri, content, httpRequest.ContentType);          
            request.Headers.Host = uri.Host;
            if (httpRequest.Headers.TryGetValue("Origin", out Microsoft.Extensions.Primitives.StringValues value))
            {
                request.Headers.Remove("Origin");
                request.Headers.Add("Origin", value.ToString());
            }
            return request;
        }

    }

    // Route configuration model
    public class RouteConfig
    {
        public string Path { get; set; }             
        public string ApiUri { get; set; }                             
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
        public static HttpRequestMessage ToHttpRequestMessage(this HttpRequest req, Uri path, HttpContent? content, string? contentType)
            => new HttpRequestMessage()
                .SetMethod(req)
                .SetAbsoluteUri(path)
                .SetHeaders(req)
                .SetContent(content, contentType);

        private static HttpRequestMessage SetAbsoluteUri(this HttpRequestMessage msg, Uri path)
            => msg.Set(m => m.RequestUri = path);

        private static HttpRequestMessage SetMethod(this HttpRequestMessage msg, HttpRequest req)
            => msg.Set(m => m.Method = new HttpMethod(req.Method));

        private static HttpRequestMessage SetHeaders(this HttpRequestMessage msg, HttpRequest req)
        {
            var excludedHeaders = new[] { "Host", "Content-Length", "Transfer-Encoding" };
            foreach (var h in req.Headers)
            {
                if (!excludedHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                {
                    msg.Headers.TryAddWithoutValidation(h.Key, h.Value.AsEnumerable());
                }
            }
            return msg;
        }

        private static HttpRequestMessage SetContent(this HttpRequestMessage msg, HttpContent? content, string? contentType = null)
        {
            if (content != null)
            {
                if (!string.IsNullOrEmpty(contentType))
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                msg.Content = content;
            }
            return msg;
        }

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