using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Post
{
    public sealed partial class AmaisBlogPostTools
    {

        [McpServerTool, Description("Create a new post with a required name. Returns the post alias (unique identifier) and name.")]
        public async Task<TextContentBlock> CreatePost([Description("Name for the new post.")] string postName)
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

                var result = await postApi.CreatePostAsync(postName);
                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = result.Data.Message,
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                            [CommonMetadataConsts.Url] = result.Data.Url,
                            [CommonPostMetadataConsts.NewPostCreatedName] = result.Data.NewPostCreatedName,
                            [CommonPostMetadataConsts.NewPostCreatedAlias] = result.Data.NewPostCreatedAlias
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
