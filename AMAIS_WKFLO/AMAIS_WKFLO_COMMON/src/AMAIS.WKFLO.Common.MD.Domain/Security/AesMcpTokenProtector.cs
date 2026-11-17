using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace AMAIS.WKFLO.Common.MD.Domain.Security
{
    /// <summary>
    /// AES-256-GCM implementation of <see cref="IMcpTokenProtector"/>. The key is a single base64-encoded
    /// 32-byte value read from the <c>MCP_OAUTH_TOKEN_ENCRYPTION_KEY</c> environment variable/configuration
    /// key, deliberately never a per-machine key ring (e.g. ASP.NET Core Data Protection's default), because
    /// MD API and AI API run as separate processes/containers and both need to decrypt the same rows.
    /// </summary>
    /// <remarks>
    /// GCM (an authenticated mode) was chosen over plain CBC specifically because these values are
    /// persisted secrets read back later, not just encrypted-in-transit data: CBC alone provides
    /// confidentiality but not integrity, so tampered ciphertext in the database would decrypt silently
    /// into garbage (or, with PKCS7 padding, open a padding-oracle side channel) instead of failing loudly.
    /// GCM's authentication tag makes any tampering — or ciphertext from an incompatible format — fail
    /// <see cref="Unprotect"/> outright with one uniform <see cref="CryptographicException"/>, no padding
    /// involved at all. This is a hard format cutover, not a versioned/dual-read scheme: any row encrypted
    /// under a prior CBC-based build of this class will fail to decrypt and must go through OAuth consent
    /// again — acceptable here since a failed decrypt already surfaces through the existing
    /// "connection issue" status/reconnect path (see McpClientService/McpOAuthConsentService), and a fresh
    /// consent overwrites the row with a newly-encrypted value regardless of what was there before.
    /// </remarks>
    public class AesMcpTokenProtector : IMcpTokenProtector
    {
        public const string ConfigurationKey = "MCP_OAUTH_TOKEN_ENCRYPTION_KEY";
        private const int KeySizeBytes = 32; // AES-256
        private const int NonceSizeBytes = 12; // recommended nonce size for AES-GCM
        private const int TagSizeBytes = 16; // AesGcm.TagByteSizes.MaxSize — strongest available integrity tag

        private readonly IConfiguration _configuration;

        // Deliberately NOT resolved/validated in the constructor: this type is reachable from the same DI
        // graph used for every chat request (via McpClientService), including tenants with no OAuth2 MCP
        // tools configured at all. Failing fast on a missing/invalid key must only happen when a caller
        // actually tries to Protect/Unprotect a token, not merely by constructing the service graph.
        private byte[] _key;

        public AesMcpTokenProtector(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _configuration = configuration;
        }

        private byte[] GetKey()
        {
            if (_key != null)
                return _key;

            var keyBase64 = _configuration[ConfigurationKey];
            if (string.IsNullOrWhiteSpace(keyBase64))
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' must be configured with a base64-encoded {KeySizeBytes * 8}-bit key to encrypt MCP OAuth tokens at rest.");

            byte[] key;
            try
            {
                key = Convert.FromBase64String(keyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"'{ConfigurationKey}' must be valid base64.", ex);
            }

            if (key.Length != KeySizeBytes)
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' must decode to exactly {KeySizeBytes} bytes ({KeySizeBytes * 8}-bit AES key), got {key.Length}.");

            return _key = key;
        }

        public string Protect(string plaintext)
        {
            if (plaintext == null)
                return null;

            using var aesGcm = new AesGcm(GetKey(), TagSizeBytes);
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[TagSizeBytes];

            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Neither the nonce nor the tag is secret; prefix/suffix them so Unprotect can recover both.
            var payload = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(cipherBytes, 0, payload, NonceSizeBytes + TagSizeBytes, cipherBytes.Length);

            return Convert.ToBase64String(payload);
        }

        public string Unprotect(string protectedValue)
        {
            if (protectedValue == null)
                return null;

            var payload = Convert.FromBase64String(protectedValue);
            if (payload.Length < NonceSizeBytes + TagSizeBytes)
                throw new InvalidOperationException("Encrypted MCP OAuth token payload is malformed.");

            var nonce = new byte[NonceSizeBytes];
            Buffer.BlockCopy(payload, 0, nonce, 0, NonceSizeBytes);

            var tag = new byte[TagSizeBytes];
            Buffer.BlockCopy(payload, NonceSizeBytes, tag, 0, TagSizeBytes);

            var cipherBytes = new byte[payload.Length - NonceSizeBytes - TagSizeBytes];
            Buffer.BlockCopy(payload, NonceSizeBytes + TagSizeBytes, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = new byte[cipherBytes.Length];
            using var aesGcm = new AesGcm(GetKey(), TagSizeBytes);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
