using AMAIS.WKFLO.Common.MD.Domain.Security;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.Security.Cryptography;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Security
{
    public class AesMcpTokenProtectorTests
    {
        private static IConfiguration BuildConfiguration(string base64Key)
        {
            var configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[AesMcpTokenProtector.ConfigurationKey]).Returns(base64Key);
            return configuration.Object;
        }

        private static string NewValidKey() => Convert.ToBase64String(new byte[32]);

        [Fact]
        public void Protect_ThenUnprotect_RoundTrips()
        {
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            var protectedValue = protector.Protect("super-secret-access-token");
            var result = protector.Unprotect(protectedValue);

            Assert.Equal("super-secret-access-token", result);
        }

        [Fact]
        public void Protect_DoesNotStorePlaintext()
        {
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            var protectedValue = protector.Protect("super-secret-access-token");

            Assert.DoesNotContain("super-secret-access-token", protectedValue);
        }

        [Fact]
        public void Protect_Null_ReturnsNull()
        {
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            Assert.Null(protector.Protect(null));
        }

        [Fact]
        public void Unprotect_Null_ReturnsNull()
        {
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            Assert.Null(protector.Unprotect(null));
        }

        [Fact]
        public void Protect_TwoCalls_ProduceDifferentCiphertext()
        {
            // Each call generates a fresh random IV, so encrypting the same plaintext twice must not
            // produce identical ciphertext (defense against pattern analysis of stored tokens).
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            var first = protector.Protect("same-value");
            var second = protector.Protect("same-value");

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Protect_MissingKey_ThrowsOnFirstUse_NotAtConstruction()
        {
            // Constructing the protector (and everything that depends on it, e.g. via DI) must never fail
            // just because the encryption key isn't configured — only an actual Protect/Unprotect call may.
            var protector = new AesMcpTokenProtector(BuildConfiguration(string.Empty));

            var exception = Assert.Throws<InvalidOperationException>(() => protector.Protect("value"));
            Assert.Contains(AesMcpTokenProtector.ConfigurationKey, exception.Message);
        }

        [Fact]
        public void Protect_WrongKeyLength_Throws()
        {
            var protector = new AesMcpTokenProtector(BuildConfiguration(Convert.ToBase64String(new byte[16])));

            Assert.Throws<InvalidOperationException>(() => protector.Protect("value"));
        }

        [Fact]
        public void Unprotect_TamperedCiphertext_ThrowsInsteadOfDecryptingSilently()
        {
            // GCM's authentication tag must reject any modification to the payload — this is the whole
            // point of moving off unauthenticated CBC. Flipping a single byte anywhere in the payload
            // (nonce, tag, or ciphertext) must fail loudly, never decrypt into garbage silently.
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));
            var protectedValue = protector.Protect("super-secret-access-token");

            var payload = Convert.FromBase64String(protectedValue);
            payload[^1] ^= 0xFF; // flip the last byte (part of the ciphertext)
            var tampered = Convert.ToBase64String(payload);

            Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(tampered));
        }

        [Fact]
        public void Unprotect_LegacyCbcFormatCiphertext_ThrowsRatherThanMisdecrypting()
        {
            // Documents the deliberate hard cutover from the old AES-CBC format: a value encrypted under
            // the previous scheme (16-byte IV + CBC/PKCS7 ciphertext, no auth tag) is not a valid GCM
            // payload and must fail clearly, not silently produce wrong plaintext. Recovery is a fresh
            // OAuth consent, which overwrites the row — see the class-level remarks on AesMcpTokenProtector.
            var protector = new AesMcpTokenProtector(BuildConfiguration(NewValidKey()));

            // Shape of the old format: 16-byte IV followed by CBC ciphertext bytes (content doesn't matter —
            // it will never pass GCM tag verification regardless).
            var legacyShapedPayload = new byte[16 + 32];
            var legacyShapedBase64 = Convert.ToBase64String(legacyShapedPayload);

            Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(legacyShapedBase64));
        }
    }
}
