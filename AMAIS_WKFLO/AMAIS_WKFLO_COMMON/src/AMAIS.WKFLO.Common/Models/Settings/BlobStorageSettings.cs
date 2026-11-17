namespace AMAIS.WKFLO.Common.Models.Settings
{
    /// <summary>
    /// Settings for Azure Blob Storage
    /// </summary>
    public class BlobStorageSettings
    {
        /// <summary>
        /// Base URL for the Azure Storage account (optional)
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Obtain or stablish the Base CDN URL to serve static resources
        /// </summary>
        public string BaseCdnUrl { get; set; }

        /// <summary>
        /// Authentication mode for SAS URL generation.
        /// "AccountKey" (default): uses the storage account key from the connection string — local computation, no network call.
        /// "ManagedIdentity" (future): uses Azure AD user delegation key — requires GetUserDelegationKeyAsync(),
        /// which must be cached at the service level (key is valid up to 7 days; refresh daily).
        /// </summary>
        public string SasAuthMode { get; set; } = "AccountKey";
    }
}