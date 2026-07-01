namespace LP.GatewayAPI.Logging
{
    public interface IAPILogger
    {
        Task LogAsync(Exception ex, string message);
    }
}
