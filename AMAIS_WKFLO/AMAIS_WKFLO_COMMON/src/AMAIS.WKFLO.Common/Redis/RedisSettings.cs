#nullable enable

namespace AMAIS.WKFLO.Common.Redis
{
    /// <summary>
    /// How this process reaches Redis, resolved from environment variables.
    /// <c>REDIS_CONNECTION_STRING</c> (local docker-compose) wins over the Azure Managed Redis variables.
    /// Never reads AZURE_CLIENT_ID or MI_CLIENT_ID: those are shared with SQL and AzureCredentialHelper and point at a different identity.
    /// </summary>
    public sealed record RedisSettings(string? ConnectionString, string? AzureEndpoint, string? AzureClientId)
    {
        public const string ConnectionStringVariable = "REDIS_CONNECTION_STRING";
        public const string AzureHostnameVariable = "AZURE_REDIS_HOSTNAME";
        public const string AzureClientIdVariable = "AZURE_REDIS_CLIENT_ID";

        // Default port of Azure Managed Redis.
        public const int DefaultAzurePort = 10000;

        public bool IsConfigured => ConnectionString is not null || AzureEndpoint is not null;

        public static RedisSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

        public static RedisSettings From(Func<string, string?> getVariable)
        {
            var connectionString = Normalize(getVariable(ConnectionStringVariable));
            var host = Normalize(getVariable(AzureHostnameVariable));
            var clientId = Normalize(getVariable(AzureClientIdVariable));

            // AZURE_REDIS_HOSTNAME is host:port; add the default port when only a host name is given.
            var endpoint = host is not null && !host.Contains(':') ? $"{host}:{DefaultAzurePort}" : host;

            return new RedisSettings(connectionString, endpoint, clientId);
        }

        private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
