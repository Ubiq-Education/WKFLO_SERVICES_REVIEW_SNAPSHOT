using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {
        [McpServerTool, Description("Update page metadata and SEO settings. Accepts page alias (required) and optional parameters: page name, menu name, meta title, meta description, OG meta title, OG meta description. Only provided fields will be updated.")]
        public async Task<TextContentBlock> UpdatePage(
            [Description("Page Alias to be updated.")] string pageAliasToUpdate,
            [Description("New Page Name for the page to be updated.")] string newPageName,
            [Description("New Menu Name for the page to be updated.")] string newMenuName,
            [Description("New Meta Title for the page to be updated.")] string newMetaTitle,
            [Description("New Meta Description for the page to be updated.")] string newMetaDescription,
            [Description("New OG Meta Title for the page to be updated.")] string newOGMetaTitle,
            [Description("New OG Meta Description for the page to be updated.")] string newOGMetaDescription
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

                var result = await pageApi.UpdatePageAsync(
                    pageAliasToUpdate,
                    newPageName,
                    newMenuName,
                    newMetaTitle,
                    newMetaDescription,
                    newOGMetaTitle,
                    newOGMetaDescription);

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
                            [CommonPageMetadataConsts.MenuName] = result.Data.MenuName,
                            [CommonPageMetadataConsts.Alias] = result.Data.Alias,
                            [CommonPageMetadataConsts.MetaTitle] = result.Data.MetaTitle,
                            [CommonPageMetadataConsts.MetadaDescription] = result.Data.MetadaDescription,
                            [CommonPageMetadataConsts.OGMetaTitle] = result.Data.OGMetaTitle,
                            [CommonPageMetadataConsts.OGMetaDescription] = result.Data.OGMetaDescription
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
