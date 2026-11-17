using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.GalleryFeature
{
    public sealed partial class AmaisGalleryFeatureTools
    {

        [McpServerTool, Description("Create a new Gallery Feature with a required name and optional tags. Returns the gallery ID needed for updating the gallery with images.")]
        public async Task<TextContentBlock> CreateGalleryFeature(
            [Description("Name for the new gallery feature.")] string name,
            [Description("Tags for the new gallery feature.")] List<string> tags
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

                var galleryFeatureApi = new GalleryFeatureApi(client, "Etc/UTC");

                var result = await galleryFeatureApi.CreateGalleryFeatureAsync(name, tags);
                if (result != null && !result.IsError)
                {
                    return new TextContentBlock()
                    {
                        Text = result.Data.Message,
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow(),
                            [CommonMetadataConsts.Id] = result.Data.Id,
                            [CommonMetadataConsts.Name] = result.Data.Name,
                            [CommonMetadataConsts.Tags] = result.Data.Tags,
                            [CommonMetadataConsts.LastUpdatedDate] = result.Data.LastUpdatedDate
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
