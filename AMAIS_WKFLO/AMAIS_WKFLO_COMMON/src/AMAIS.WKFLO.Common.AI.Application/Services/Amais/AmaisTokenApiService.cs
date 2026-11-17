using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Models.Results.Token;
using AMAIS.WKFLO.Common.Models.Settings;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Services.Amais
{
    public class AmaisTokenApiService : IAmaisTokenApiService
    {
        private readonly ILogger<AmaisTokenApiService> _logger;
        private readonly AmaisTokenVoiceApiServiceSettings _amaisTokenApiServiceSettings;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public AmaisTokenApiService(ILogger<AmaisTokenApiService> logger, IOptions<AmaisTokenVoiceApiServiceSettings> amaisTokenApiServiceSettings
            , TenantContext tenantContext, UserContext userContext)
        {
            _logger = logger;
            _amaisTokenApiServiceSettings = amaisTokenApiServiceSettings.Value;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async Task<string> GetVoiceTokenAsync()
        {
            var url = _amaisTokenApiServiceSettings.TokenServiceApiURL;
            using var httpClient = await GetHttpClientAsync();
            string urlMethod = $"{url}/generate-token";

            var payload = new
            {
                site_id = _tenantContext.TenantName.ToLower()
            };

            string jsonString = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(urlMethod, content);

            if (response.IsSuccessStatusCode)
            {
                string contentString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TokenResponseResult>(contentString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result.Token;
            }
            else
            {
                // Log the error details
                _logger.LogError($"Error getting the token from Amais Token Service: {(int)response.StatusCode} - {response.ReasonPhrase}", await response.Content.ReadAsStringAsync());
                throw new HttpRequestException($"Error getting the token from Amais Token Service: {(int)response.StatusCode} - {response.ReasonPhrase}");
            }
        }

        public async Task<string> GetAmaisIntegrationTokenAsync()
        {
            var url = _amaisTokenApiServiceSettings.TokenServiceApiURL;
            using var httpClient = await GetHttpClientAsync();
            string urlMethod = $"{url}/generate-token";
            byte[] hash;
            using (MD5 md5 = MD5.Create())
            {
                md5.Initialize();
                md5.ComputeHash(Encoding.UTF8.GetBytes(_userContext.Email));
                hash = md5.Hash;
            }
            var payload = new
            {
                site_id = _tenantContext.TenantName.ToLower(),
                claims = new Dictionary<string, string>
                {
                    { "aud", Environment.GetEnvironmentVariable("AMAIS_URL_TEST") },
                    { "sub",  Convert.ToBase64String(hash) },
                    { ClaimTypes.Name, _userContext.UserName },
                    { ClaimTypes.GivenName, _userContext.GivenName },
                    { ClaimTypes.Surname, _userContext.Surname },
                    { ClaimTypes.Email, _userContext.Email },
                    { ClaimTypes.Role, Environment.GetEnvironmentVariable("AMAIS_ROLE_TEST")  }
                }
            };

            string jsonString = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(urlMethod, content);


            if (response.IsSuccessStatusCode)
            {
                string contentString = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<TokenResponseResult>(contentString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result.Token;
            }
            else
            {
                // Log the error details
                _logger.LogError($"Error getting the token from Amais Token Service: {(int)response.StatusCode} - {response.ReasonPhrase}", await response.Content.ReadAsStringAsync());
                throw new HttpRequestException($"Error getting the token from Amais Token Service: {(int)response.StatusCode} - {response.ReasonPhrase}");
            }
        }

        private async Task<HttpClient> GetHttpClientAsync()
        {
            var credential = AzureCredentialHelper.Create();

            var appIdUri = _amaisTokenApiServiceSettings.AppIdUri;
            var tokenRequestContext = new TokenRequestContext(new[] { $"{appIdUri}/.default" });

            var accessToken = await credential.GetTokenAsync(tokenRequestContext);
            var token = accessToken.Token;

            _logger.LogInformation($"Token expires: {accessToken.ExpiresOn}");

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return httpClient;
        }

    }
}
