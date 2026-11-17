using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Post
{
    public sealed partial class AmaisBlogPostTools
    {
        [McpServerTool, Description("Update a post's featured/main image by uploading a new image file. Requires post alias, image byte array, file name, file MIME type (e.g., image/jpeg, image/png).")]
        public async Task<TextContentBlock> UpdatePostImage(
            [Description("Post Alias to be updated.")] string postAliasToUpdate,
            [Description("New image in bytes for the post to be updated.")] byte[] newImageBytes,
            [Description("New image name for the post to be updated.")] string newImageName,
            [Description("New image mime type for the post to be updated.")] string newImageMimeType
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

                var result = await postApi.UpdatePostImageByImage(
                    postAliasToUpdate,
                    newImageName,
                    newImageBytes,
                    newImageMimeType
                );

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
