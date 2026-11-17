using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MD.Application.Agents.Common
{
    public static class AgentSecurityConfigurationHelper
    {
        private const string JwtTokenCallbackType = "jwt-token-callback";
        private const string DefaultWebOriginsEnvironmentVariable = "DEFAULT_WEB_ORIGINS";

        public static string ApplyDefaultJwtDomains(string securityConfigurationJson, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(securityConfigurationJson))
            {
                return securityConfigurationJson;
            }

            AgentSecurityConfiguration securityConfiguration;
            try
            {
                securityConfiguration = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(securityConfigurationJson);
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Could not parse AgentChatSetting.SecurityConfiguration as JSON; leaving it unchanged");
                return securityConfigurationJson;
            }

            if (securityConfiguration == null ||
                !string.Equals(securityConfiguration.Type, JwtTokenCallbackType, StringComparison.OrdinalIgnoreCase))
            {
                return securityConfigurationJson;
            }

            var defaultDomains = GetDefaultDomains();
            if (defaultDomains.Length == 0)
            {
                return securityConfigurationJson;
            }

            securityConfiguration.Jwt ??= new JwtConfiguration();
            securityConfiguration.Jwt.Domains = (securityConfiguration.Jwt.Domains ?? [])
                .Concat(defaultDomains)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return JsonConvert.SerializeObject(securityConfiguration);
        }

        private static string[] GetDefaultDomains()
        {
            var raw = Environment.GetEnvironmentVariable(DefaultWebOriginsEnvironmentVariable);
            if (string.IsNullOrEmpty(raw))
            {
                return [];
            }

            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeDomain)
                .ToArray();
        }

        private static string NormalizeDomain(string value)
        {
            var trimmed = value.Trim();
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Authority)
                ? uri.Authority
                : trimmed;
        }
    }
}
