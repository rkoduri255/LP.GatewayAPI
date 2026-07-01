namespace LP.GatewayAPI.Utilities
{
    public class CryptographyOptions
    {
        public string PassPhrase { get; set; } = string.Empty;
        public string SaltValue { get; set; } = string.Empty;
        public string InitVector { get; set; } = string.Empty;
        public int PasswordIterations { get; set; } = 2;
    }
}
