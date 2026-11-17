using Azure.Identity;

namespace AMAIS.WKFLO.Common.Helpers
{
    /// <summary>
    /// Resolves the correct <see cref="DefaultAzureCredential"/> based on the runtime environment:
    /// managed identity (MI_CLIENT_ID env var) → Visual Studio / Azure CLI (local dev).
    /// </summary>
    public static class AzureCredentialHelper
    {
        /// <summary>
        /// Returns a credential scoped to the user-assigned managed identity identified by <paramref name="clientId"/>.
        /// </summary>
        public static DefaultAzureCredential CreateManagedIdentityCredential(string clientId) =>
            new(new DefaultAzureCredentialOptions
            {
                ExcludeAzureCliCredential = true,
                ExcludeAzureDeveloperCliCredential = true,
                ExcludeAzurePowerShellCredential = true,
                ExcludeEnvironmentCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeManagedIdentityCredential = false,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeVisualStudioCredential = true,
                ManagedIdentityClientId = clientId
            });

        /// <summary>
        /// Returns a credential that only tries Visual Studio and Azure CLI — safe for local dev.
        /// </summary>
        public static DefaultAzureCredential CreateDevCredential() =>
            new(new DefaultAzureCredentialOptions
            {
                ExcludeAzureCliCredential = false,
                ExcludeAzureDeveloperCliCredential = true,
                ExcludeAzurePowerShellCredential = true,
                ExcludeEnvironmentCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeVisualStudioCredential = false
            });

        /// <summary>
        /// Picks the right credential automatically: managed identity when <c>MI_CLIENT_ID</c>
        /// is set, otherwise dev credentials.
        /// </summary>
        public static DefaultAzureCredential Create()
        {
            var managedIdentityClientId = Environment.GetEnvironmentVariable("MI_CLIENT_ID");
            return !string.IsNullOrEmpty(managedIdentityClientId)
                ? CreateManagedIdentityCredential(managedIdentityClientId)
                : CreateDevCredential();
        }
    }
}
