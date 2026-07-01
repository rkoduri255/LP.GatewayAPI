using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace LP.GatewayAPI.Utilities
{
    public sealed class Cryptography
    {
        private readonly CryptographyOptions _options;

        public Cryptography(IOptions<CryptographyOptions> options)
        {
            _options = options.Value;
        }

        public string DecryptToken(string token) => DecryptPBKDF2(token);

        public string DecryptPBKDF2(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentException("cipherText cannot be null or empty.");

            byte[] initVectorBytes = Encoding.ASCII.GetBytes(_options.InitVector);
            byte[] saltValueBytes = Encoding.ASCII.GetBytes(_options.SaltValue);
            byte[] cipherTextBytes = Convert.FromBase64String(cipherText);

            using var password = new Rfc2898DeriveBytes(
                _options.PassPhrase, saltValueBytes, _options.PasswordIterations, HashAlgorithmName.SHA1);
            byte[] keyBytes = password.GetBytes(16);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keyBytes;
            aes.IV = initVectorBytes;

            using var decryptor = aes.CreateDecryptor();
            using var memoryStream = new MemoryStream(cipherTextBytes);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var resultStream = new MemoryStream();
            cryptoStream.CopyTo(resultStream);
            return Encoding.UTF8.GetString(resultStream.ToArray());
        }
    }
}
