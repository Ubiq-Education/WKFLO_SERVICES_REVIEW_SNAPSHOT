using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace AMAIS.WKFLO.Common.Helpers
{
    public static class JwtKeyResolver
    {
        /// <summary>
        /// Registers <see cref="KeyClient"/> and <see cref="RsaSecurityKey"/> as singletons and
        /// returns the resolved key so it can be passed directly into JWT bearer options.
        /// Returns null when <c>CENTRAL_KEY_VAULT_URI</c> or <c>AmaisJwtScheme:KeyName</c> are not configured.
        /// </summary>
        public static RsaSecurityKey Register(IServiceCollection services, IConfiguration configuration)
        {
            var vaultUri = Environment.GetEnvironmentVariable("CENTRAL_KEY_VAULT_URI");
            var keyName = configuration["AmaisJwtScheme:KeyName"];

            if (string.IsNullOrEmpty(vaultUri) || string.IsNullOrEmpty(keyName))
                return null;

            var keyClient = new KeyClient(new Uri(vaultUri), AzureCredentialHelper.Create());
            services.AddSingleton(keyClient);

            var kvKey = keyClient.GetKey(keyName).Value;
            var rsaParams = new RSAParameters
            {
                Modulus = kvKey.Key.N,
                Exponent = kvKey.Key.E
            };
            var rsaFromVault = RSA.Create();
            rsaFromVault.ImportParameters(rsaParams);
            var securityKey = new RsaSecurityKey(rsaFromVault);

            services.AddSingleton(securityKey);
            return securityKey;
        }
    }
}
