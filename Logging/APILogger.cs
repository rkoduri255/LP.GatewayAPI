using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace LP.GatewayAPI.Logging
{
    public class APILogger : IAPILogger
    {
        private const string LocalEnvironment = "local";

        private readonly APILoggerOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<APILogger> _logger;

        public APILogger(
            IOptions<APILoggerOptions> options,
            IHttpClientFactory httpClientFactory,
            ILogger<APILogger> logger)
        {
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task LogAsync(Exception ex, string message)
        {
            if (_options.Environment?.Equals(LocalEnvironment, StringComparison.OrdinalIgnoreCase) == true)
                return;

            try
            {
                var logItem = new LogItem
                {
                    Timestamp = DateTime.UtcNow,
                    Type = "Error",
                    Message = message,
                    AppName = _options.AppName,
                    Environment = _options.Environment,
                    Project = _options.Project,
                    Error = ex?.ToString()
                };

                var uri = new Uri($"{_options.APIBaseURL}/add-log");
                var content = new StringContent(JsonConvert.SerializeObject(logItem), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

                var client = _httpClientFactory.CreateClient();
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Remote log endpoint returned {StatusCode}", (int)response.StatusCode);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to send log entry to remote endpoint");
            }
        }
    }
}
