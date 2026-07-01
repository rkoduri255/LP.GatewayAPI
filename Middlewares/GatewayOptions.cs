namespace LP.GatewayAPI.Middlewares
{
    public class GatewayOptions
    {
        public bool DisableSslValidation { get; set; }
        public int RequestTimeoutSeconds { get; set; } = 30;
    }
}
