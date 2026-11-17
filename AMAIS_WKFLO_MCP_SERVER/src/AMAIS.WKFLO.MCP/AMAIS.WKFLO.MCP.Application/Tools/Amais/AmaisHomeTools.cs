using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais
{
    [McpServerToolType]
    [Authorize]
    public sealed class AmaisHomeTools
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IDateTimeProvider _dateTimeProvider;

        public AmaisHomeTools(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor, IDateTimeProvider dateTimeProvider)
        {
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _dateTimeProvider = dateTimeProvider;
        }

        [McpServerTool, Description("Get Welcome message from AMAIS.")]
        public async Task<TextContentBlock> GetWelcomeMessage()
        {
            if (_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                var principal = _httpContextAccessor.HttpContext.User;
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.FirstOrDefault();

                var client = _httpClientFactory.CreateClient("AmaisApi");
                client.BaseAddress = new Uri(principal.FindFirst("aud")?.Value);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT_HEADER"));

                var homeApi = new HomeApi(client, "Etc/UTC");

                var successfulResult = await homeApi.WelcomeMessageAsync();
                if (successfulResult != null)
                {
                    return new TextContentBlock()
                    {
                        Text = successfulResult.Data.Message,
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
                        }
                    };
                }
                else
                {
                    return new TextContentBlock()
                    {
                        Text = "Empty result.",
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
                        }
                    };
                }
            }

            return new TextContentBlock()
            {
                Text = "User is not authenticated.",
                Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
                }
            };
        }
    }
}
