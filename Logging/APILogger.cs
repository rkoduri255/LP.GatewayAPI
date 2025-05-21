using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace LP.GatewayAPI.Logging
{
    public class APILogger : IAPILogger
    {
        private const string LOCAL_ENVIRONMENT = "local";
        public readonly APILoggerOptions _apiLoggerOptions;

        public APILogger(IOptions<APILoggerOptions> options)
        {
            _apiLoggerOptions = options.Value;
        }


        public void Log(Exception ex, string message)
        {
            var environment = _apiLoggerOptions.Environment;
            if (environment == null || environment.Equals(LOCAL_ENVIRONMENT, StringComparison.CurrentCultureIgnoreCase))
            {
                return;
            }

            Uri uri = new($"{_apiLoggerOptions.APIBaseURL}/add-log");
            #pragma warning disable CS8604 // Possible null reference argument.
            var logItem = CreateLogItem(message, ex?.ToString(), LogLevel.Error, environment);
            #pragma warning restore CS8604 // Possible null reference argument.
            var requestMesage = new HttpRequestMessage(HttpMethod.Post, uri);
            var stringContent = new StringContent(JsonConvert.SerializeObject(logItem), Encoding.UTF8, "application/json");
            requestMesage.Content = stringContent;
            var client = new HttpClient();
            client.SendAsync(requestMesage);

        }

        /// <summary>
        /// Factory method for creating instance of LogItem object for errors
        /// </summary>
        /// <param name="message"></param>
        /// <param name="error"></param>
        /// <param name="logLevel"></param>
        /// <returns></returns>
        private LogItem CreateLogItem(string message, string error, LogLevel logLevel, string environment)
        {
            var logMessageEntry = new LogItem
            {
                Type = logLevel.ToString(),
                Message = message,
                AppName = _apiLoggerOptions.AppName,
                Environment = environment,
                Project = _apiLoggerOptions.Project,
                Error = error,
                Timestamp = DateTime.Now
            };
            return logMessageEntry;
        }        
    }
}
