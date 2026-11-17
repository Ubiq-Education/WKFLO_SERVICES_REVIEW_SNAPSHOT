using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {
        [McpServerTool, Description("Update a page's featured/main image by assigning an existing rendition ID. Useful for reusing images already in the system. Requires page alias and valid rendition ID.")]
        public async Task<TextContentBlock> UpdatePageImageById(
            [Description("Page Alias to be updated.")] string pageAliasToUpdate,
            [Description("Existing rendition id for page image to be updated.")] int renditionId
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

                var result = await pageApi.UpdatePageImageByRenditionId(
                    pageAliasToUpdate,
                    renditionId);

                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = result.Data.Message,
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                            [CommonMetadataConsts.Url] = result.Data.Url,
                            [CommonPageMetadataConsts.PageName] = result.Data.PageName,
                            [CommonPageMetadataConsts.PageImageName] = result.Data.PageImageName,
                            [CommonPageMetadataConsts.PageImagePath] = result.Data.PageImagePath
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
