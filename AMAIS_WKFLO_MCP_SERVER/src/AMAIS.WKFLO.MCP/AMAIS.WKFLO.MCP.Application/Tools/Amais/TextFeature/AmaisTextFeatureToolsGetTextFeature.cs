using AMAIS.WKFLO.MCP.Application.Apis.Amais.TextFeature;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.TextFeature
{
    public sealed partial class AmaisTextFeatureTools
    {
        [McpServerTool, Description("Retrieve the saved and published text content for a specific feature by its ID. Returns feature ID, status, saved text, published text, and published date.")]
        public async Task<TextContentBlock> GetTextFeature(
            [Description("ID of the feature to retrieve the text content for.")] int id)
        {
            if (_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                var principal = _httpContextAccessor.HttpContext.User;
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.FirstOrDefault();

                var client = _httpClientFactory.CreateClient("AmaisApi");
                client.BaseAddress = new Uri(principal.FindFirst("aud")?.Value);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT_HEADER"));

                var textFeatureApi = new TextFeatureApi(client, "Etc/UTC");

                var result = await textFeatureApi.GetTextFeatureAsync(id);
                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = JsonConvert.SerializeObject(result.Data),
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                            [CommonMetadataConsts.Id] = result.Data.Id
                        }
                    };
                }
                else if (result != null && result.IsError)
                {
                    return GetTextContentBlockForErrorResult(result.ErrorMessage);
                }
                else
                {
                    return GetTextContentBlockForEmptyResult();
                }
            }

            return GetTextContentBlockForUserNotAuthorized();
        }
    }
}
