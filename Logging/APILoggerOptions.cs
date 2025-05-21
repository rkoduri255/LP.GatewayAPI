namespace LP.GatewayAPI.Logging
{
    public class APILoggerOptions
    {
        public virtual string Environment { get; set; }

        public virtual string Project { get; set; }

        public virtual string AppName { get; set; }

        public virtual string APIBaseURL { get; set; }
    }
}
