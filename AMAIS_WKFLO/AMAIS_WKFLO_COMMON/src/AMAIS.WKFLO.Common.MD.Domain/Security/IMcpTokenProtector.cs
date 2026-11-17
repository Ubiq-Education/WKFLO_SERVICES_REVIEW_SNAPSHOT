namespace AMAIS.WKFLO.Common.MD.Domain.Security
{
    /// <summary>
    /// Encrypts/decrypts the OAuth secrets persisted on <see cref="Entities.ToolOAuthToken"/>
    /// (access token, refresh token, dynamically-registered client secret) so they are never stored in
    /// plain text. Implementations must be usable interchangeably from every service that reads/writes
    /// this table (MD API's OAuth consent endpoints and the AI API's silent token refresh path), so the
    /// key material must come from shared configuration rather than a per-process key ring.
    /// </summary>
    public interface IMcpTokenProtector
    {
        /// <summary>Encrypts <paramref name="plaintext"/>. Returns null unchanged.</summary>
        string Protect(string plaintext);

        /// <summary>Decrypts a value previously returned by <see cref="Protect"/>. Returns null unchanged.</summary>
        string Unprotect(string protectedValue);
    }
}
