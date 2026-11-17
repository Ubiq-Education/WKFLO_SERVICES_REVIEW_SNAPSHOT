using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {
        [McpServerTool, Description("Retrieve all available page templates ordered by popularity (most used first). Returns a list of templates with their name and page count.")]
        public async Task<TextContentBlock> GetPageTemplates()
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

                var result = await pageApi.GetTemplatesAsync();
                if (result != null && !result.IsError)
                {
                    var sorted = SortByPopularity(result.Data?.Templates);
                    return new TextContentBlock()
                    {
                        Text = JsonConvert.SerializeObject(sorted),
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

        public static List<GetTemplateData> SortByPopularity(IReadOnlyList<GetTemplateData> templates)
            => templates?.OrderByDescending(t => t.PageCount).ToList();
    }
}
