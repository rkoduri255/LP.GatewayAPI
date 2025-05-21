using Newtonsoft.Json;

namespace LP.GatewayAPI.Logging
{
    public class LogItem
    {
        public DateTime Timestamp { get; set; }
        public string Environment { get; set; }
        public string Project { get; set; }
        [JsonProperty("app_name")]
        public string AppName { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }
    }
}
