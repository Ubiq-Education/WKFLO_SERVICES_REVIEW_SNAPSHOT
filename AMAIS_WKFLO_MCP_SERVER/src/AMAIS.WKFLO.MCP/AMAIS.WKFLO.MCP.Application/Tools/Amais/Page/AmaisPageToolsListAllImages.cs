using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {

        [McpServerTool, Description("Search and retrieve all available page images with their metadata. Accepts search terms to filter images by name or other attributes. Returns image details including file names, last updated dates, and rendition IDs.")]
        public async Task<TextContentBlock> ListAllPageImages(
            [Description("Search terms to filter available page images.")] string searchTerms
            )
        {
            if (_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                var principal = _httpContextAccessor.HttpContext.User;
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.FirstOrDefault();

                var client = _httpClientFactory.CreateClient("AmaisApi");
                client.BaseAddress = new Uri(principal.FindFirst("aud")?.Value);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT_HEADER"));

                var pageApi = new PageApi(client, "Etc/UTC");

                var result = await pageApi.ListAvailablePageImagesAsync(searchTerms);
                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = JsonConvert.SerializeObject(result.Data),
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
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
