using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Post
{
    public sealed partial class AmaisBlogPostTools
    {
        [McpServerTool, Description("Update a post's featured/main image by assigning an existing rendition ID. Useful for reusing images already in the system. Requires post alias and valid rendition ID.")]
        public async Task<TextContentBlock> UpdatePostImageById(
            [Description("Post Alias to be updated.")] string postAliasToUpdate,
            [Description("Existing rendition id for post image to be updated.")] int renditionId
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

                var result = await postApi.UpdatePostImageByRenditionId(
                    postAliasToUpdate,
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
                            [CommonPostMetadataConsts.PostName] = result.Data.PostName,
                            [CommonPostMetadataConsts.PostImageName] = result.Data.PostImageName,
                            [CommonPostMetadataConsts.PostImagePath] = result.Data.PostImagePath
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
