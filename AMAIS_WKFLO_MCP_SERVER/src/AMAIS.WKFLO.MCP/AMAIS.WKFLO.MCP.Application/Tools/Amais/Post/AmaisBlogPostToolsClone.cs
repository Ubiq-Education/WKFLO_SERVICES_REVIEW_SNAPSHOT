using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Post
{
    public sealed partial class AmaisBlogPostTools
    {

        [McpServerTool, Description("Clone an existing post by its alias to create a duplicate with a new name.")]
        public async Task<TextContentBlock> ClonePost(
            [Description("Post alias to be cloned.")] string postAliasToClone,
            [Description("New Name for the new post.")] string newPostName,
            [Description("If true, clears the meta title/description/keywords, the settings description/image and the OpenGraph (social) title/description/image on the cloned post. Layout and structural settings are preserved. Defaults to false, which copies the Settings, SEO and Social content.")] bool clearSeoAndSocialMetadata = false,
            [Description("If true, features used only on the source post get their own independent versions on the clone; features already shared with other pages or layouts stay shared. Defaults to false, which keeps all features shared (linked) between the source post and the clone.")] bool versionUnsharedFeatures = false)
        {
            if (_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                var principal = _httpContextAccessor.HttpContext.User;
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.FirstOrDefault();

                var client = _httpClientFactory.CreateClient("AmaisApi");
                client.BaseAddress = new Uri(principal.FindFirst("aud")?.Value);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT_HEADER"));

                var postApi = new PostApi(client, "Etc/UTC");

                var result = await postApi.ClonePostAsync(postAliasToClone, newPostName,
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
                            [CommonPostMetadataConsts.NewPostClonedName] = result.Data.NewPostClonedName,
                            [CommonPostMetadataConsts.NewPostClonedAlias] = result.Data.NewPostClonedAlias
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
