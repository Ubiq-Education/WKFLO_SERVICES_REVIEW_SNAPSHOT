using System.Security.Cryptography;
using System.Text;

namespace AMAIS.WKFLO.Common.Helpers
{
    /// <summary>
    /// Log-safe references to blob names. Blob names can end with a user's original file name, which must not be logged.
    /// </summary>
    public static class BlobNameLogFormatter
    {
        /// <summary>
        /// The blob's folders with the file name replaced by "…", plus the first 8 hex chars of the SHA-256 of the full
        /// name, e.g. <c>{tenantGuid}/{sessionId}/… [#a1b2c3d4]</c>: enough to see where something happened and to
        /// correlate repeated events on the same blob. A name without folders gives the fingerprint only; a missing
        /// name gives <c>(none)</c>.
        /// </summary>
        public static string ForLog(string blobName)
        {
            if (string.IsNullOrEmpty(blobName))
                return "(none)";

            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(blobName)))[..8];
            var lastSlash = blobName.LastIndexOf('/');

            return lastSlash < 0 ? $"[#{fingerprint}]" : $"{blobName[..(lastSlash + 1)]}… [#{fingerprint}]";
        }
    }
}
