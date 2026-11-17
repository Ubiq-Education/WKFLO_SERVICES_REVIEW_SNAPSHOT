using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.GalleryFeature
{
    public sealed partial class AmaisGalleryFeatureTools
    {
        [McpServerTool, Description("Update Gallery Feature by adding new images, rendition IDs, or tags. Requires gallery ID. You can add images by file name/type OR by existing rendition IDs.")]
        public async Task<TextContentBlock> UpdateGalleryFeature(
            [Description("Gallery feature id to be updated.")] int id,
            [Description("Gallery name to be updated.")] string name,
            [Description("New tags to be assigned.")] List<string> tags,
            [Description("New images to be added.")] List<GalleryImageRequest> images,
            [Description("New rendition ids to be added.")] List<int> renditionIds,
            [Description(@"Image size strategy, ask for strategy if the gallery has a preset ids greater than 0. Possible values: 0 = Default, 1 = Preset, 2 = Custom.
                           Note: Default option means it will use the image default size.
                           Conditions:
                            1 - If Preset is chosen then Preset ID must be provided.
                            2 - If Custom is chosen then Image Width and Image Height must be provided.")] int imageSizeStrategy,
            [Description("Preset ID.")] int? presetId = null,
            [Description("Image width.")] int? imageWidth = null,
            [Description("Image height.")] int? imageHeight = null
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

                var pageApi = new GalleryFeatureApi(client, "Etc/UTC");

                var result = await pageApi.UpdateGalleryFeatureAsync(
                    id,
                    name,
                    tags,
                    images,
                    renditionIds,
                    imageSizeStrategy,
                    presetId,
                    imageWidth,
                    imageHeight);

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
                            [CommonGalleryFeatureMetadataConsts.GalleryImages] = JsonConvert.SerializeObject(result.Data.GalleryImages ?? []),
                            [CommonMetadataConsts.LastUpdatedDate] = result.Data.LastUpdatedDate,
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
