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
            if (environment == null || environment.ToLower() == LOCAL_ENVIRONMENT)
            {
                return;
            }

            try
            {
                Uri uri = new($"{_apiLoggerOptions.APIBaseURL}/add-log");
                var logItem = CreateLogItem(message, ex?.ToString(), LogLevel.Error, environment);
                var requestMesage = new HttpRequestMessage(HttpMethod.Post, uri);
                var stringContent = new StringContent(JsonConvert.SerializeObject(logItem), Encoding.UTF8, "application/json");
                requestMesage.Content = stringContent;
                var client = new HttpClient();
                client.SendAsync(requestMesage).ContinueWith(LogFailedMessages);
            }
            catch
            {
            }
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

        /// <summary>
        /// This method will log error messages when there is error response from Log API, this can be considered
        /// as fallback mechanism to make sure that we don't miss logs in any case.
        /// </summary>
        /// <param name="task"></param>
        private void LogFailedMessages(Task<HttpResponseMessage> responseTask)
        {
            var response = responseTask.Result;
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var message = @$"Failed to push logs to log API. Response statuscode is [{response.StatusCode}] and reason phrase is [{response.ReasonPhrase}]";
            Console.WriteLine(message);
            //TODO : Log error messages coming from log API as well
        }


    }
}
