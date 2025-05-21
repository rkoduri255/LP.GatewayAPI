namespace LP.GatewayAPI.Logging
{
    public interface IAPILogger
    {
        void Log(Exception ex, string message);
    }
}
