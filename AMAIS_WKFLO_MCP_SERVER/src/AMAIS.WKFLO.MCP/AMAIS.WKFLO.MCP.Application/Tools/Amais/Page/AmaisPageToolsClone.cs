using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {

        [McpServerTool, Description("Clone an existing page by its alias to create a duplicate with a new name.")]
        public async Task<TextContentBlock> ClonePage(
            [Description("Page alias to be cloned.")] string pageAliasToClone,
            [Description("New Name for the new page.")] string newPageName,
            [Description("If true, clears the meta title/description/keywords, the settings description/image and the OpenGraph (social) title/description/image on the cloned page. Layout and structural settings are preserved. Defaults to false, which copies the Settings, SEO and Social content.")] bool clearSeoAndSocialMetadata = false,
            [Description("If true, features used only on the source page get their own independent versions on the clone; features already shared with other pages or layouts stay shared. Defaults to false, which keeps all features shared (linked) between the source page and the clone.")] bool versionUnsharedFeatures = false)
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

                var result = await pageApi.ClonePageAsync(pageAliasToClone, newPageName,
                    clearSeoAndSocialMetadata: clearSeoAndSocialMetadata,
                    versionUnsharedFeatures: versionUnsharedFeatures);
                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = result.Data.Message,
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                            [CommonMetadataConsts.Url] = result.Data.Url,
                            [CommonPageMetadataConsts.NewPageClonedName] = result.Data.NewPageClonedName,
                            [CommonPageMetadataConsts.NewPageClonedAlias] = result.Data.NewPageClonedAlias
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
