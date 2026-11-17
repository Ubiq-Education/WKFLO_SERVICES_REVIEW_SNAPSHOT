using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Post
{
    public sealed partial class AmaisBlogPostTools
    {
        [McpServerTool, Description("Update post metadata and SEO settings. Accepts post alias (required) and optional parameters: post name, menu name, meta title, meta description, OG meta title, OG meta description. Only provided fields will be updated.")]
        public async Task<TextContentBlock> UpdatePost(
            [Description("Post Alias to be updated.")] string postAliasToUpdate,
            [Description("New Post Name for the post to be updated.")] string newPostName,
            [Description("New Menu Name for the post to be updated.")] string newMenuName,
            [Description("New Meta Title for the post to be updated.")] string newMetaTitle,
            [Description("New Meta Description for the post to be updated.")] string newMetaDescription,
            [Description("New OG Meta Title for the post to be updated.")] string newOGMetaTitle,
            [Description("New OG Meta Description for the post to be updated.")] string newOGMetaDescription
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

                var postApi = new PostApi(client, "Etc/UTC");

                var result = await postApi.UpdatePostAsync(
                    postAliasToUpdate,
                    newPostName,
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
                            [CommonPostMetadataConsts.PostName] = result.Data.PostName,
                            [CommonPostMetadataConsts.MenuName] = result.Data.MenuName,
                            [CommonPostMetadataConsts.Alias] = result.Data.Alias,
                            [CommonPostMetadataConsts.MetaTitle] = result.Data.MetaTitle,
                            [CommonPostMetadataConsts.MetadaDescription] = result.Data.MetadaDescription,
                            [CommonPostMetadataConsts.OGMetaTitle] = result.Data.OGMetaTitle,
                            [CommonPostMetadataConsts.OGMetaDescription] = result.Data.OGMetaDescription
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
