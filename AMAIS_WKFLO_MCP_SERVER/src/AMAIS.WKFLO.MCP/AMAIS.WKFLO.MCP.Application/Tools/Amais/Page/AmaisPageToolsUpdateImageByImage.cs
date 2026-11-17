using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.Page
{
    public sealed partial class AmaisPageTools
    {
        [McpServerTool, Description("Update a page's featured/main image by uploading a new image file. Requires page alias, image byte array, file name, and file MIME type (e.g., image/jpeg, image/png).")]
        public async Task<TextContentBlock> UpdatePageImage(
            [Description("Page Alias to be updated.")] string pageAliasToUpdate,
            [Description("New image in bytes for the page to be updated.")] byte[] newImageBytes,
            [Description("New image name for the page to be updated.")] string newImageName,
            [Description("New image mime type for the page to be updated.")] string newImageMimeType
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

                var result = await pageApi.UpdatePageImageByImage(
                    pageAliasToUpdate,
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
