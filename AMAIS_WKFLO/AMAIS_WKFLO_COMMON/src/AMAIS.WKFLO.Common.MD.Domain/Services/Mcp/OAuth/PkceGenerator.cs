using System.Security.Cryptography;
using System.Text;

namespace AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth
{
    /// <summary>
    /// PKCE (RFC 7636) verifier/challenge generation and OAuth `state` generation for the hand-rolled MCP
    /// OAuth 2.1 consent flow in <see cref="McpOAuthConsentService"/>. Pure/static — no HTTP, no persistence.
    /// </summary>
    public static class PkceGenerator
    {
        // RFC 7636 recommends at least 32 random bytes (256 bits) of entropy for the code_verifier.
        private const int VerifierEntropyBytes = 32;

        // The `state` parameter has no RFC-mandated size, but is generated with the same entropy as the
        // code_verifier for a consistent, comfortably CSRF-resistant margin.
        private const int StateEntropyBytes = 32;

        /// <summary>Generates a cryptographically random PKCE code_verifier (RFC 7636 §4.1), base64url-encoded.</summary>
        public static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(VerifierEntropyBytes));

        /// <summary>Computes the S256 PKCE code_challenge (RFC 7636 §4.2) for a given code_verifier.</summary>
        public static string ComputeCodeChallenge(string codeVerifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        /// <summary>Generates a cryptographically random OAuth `state` value for CSRF protection.</summary>
        public static string GenerateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(StateEntropyBytes));

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
