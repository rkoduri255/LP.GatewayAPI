using System.Security.Cryptography;
using System.Text;

namespace LP.GatewayAPI.Utilities
{
    public sealed class Cryptography
    {
        private static readonly string PassPhrase = "rj11fxc8$";
        private static readonly string SaltValue = "uwts247$";
        private static readonly int PasswordIterations = 2;
        private static readonly string InitVector = "@1B2c3D4e5F6g7H8";

        public string DecryptToken(string token)
        {
            return DecryptPBKDF2(token);
        }

        public string DecryptPBKDF2(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentException("Invalid Argument, cipherText cannot be null or empty.");

            byte[] initVectorBytes = Encoding.ASCII.GetBytes(InitVector);
            byte[] saltValueBytes = Encoding.ASCII.GetBytes(SaltValue);
            byte[] cipherTextBytes = Convert.FromBase64String(cipherText);

            using (var password = new Rfc2898DeriveBytes(PassPhrase, saltValueBytes, PasswordIterations, HashAlgorithmName.SHA1))
            {
                byte[] keyBytes = password.GetBytes(16); // AES-128 requires 16 bytes key

                using (Aes aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7; // Ensure padding is handled
                    aes.Key = keyBytes;
                    aes.IV = initVectorBytes;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var memoryStream = new MemoryStream(cipherTextBytes))
                    using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                    using (var resultStream = new MemoryStream())
                    {
                        byte[] buffer = new byte[1024]; // Read in chunks
                        int bytesRead;
                        while ((bytesRead = cryptoStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            resultStream.Write(buffer, 0, bytesRead);
                        }

                        return Encoding.UTF8.GetString(resultStream.ToArray());
                    }
                }
            }
        }

    }
}