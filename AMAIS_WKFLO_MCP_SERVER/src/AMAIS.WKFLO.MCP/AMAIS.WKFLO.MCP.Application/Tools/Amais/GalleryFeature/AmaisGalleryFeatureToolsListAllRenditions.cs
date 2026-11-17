using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MCP.Application.Tools.Amais.GalleryFeature
{
    public sealed partial class AmaisGalleryFeatureTools
    {

        [McpServerTool, Description("Search available renditions (images and videos) across all galleries by search terms, or resolve known rendition IDs (e.g. the renditionId values returned by get_gallery_feature or list_gallery_features) to their URLs and names. Returns rendition IDs, URLs, names and last updated dates, but does not indicate which gallery each rendition belongs to. When renditionIds are given, only those renditions are returned and any IDs that could not be resolved are listed in notFoundRenditionIds.")]
        public async Task<TextContentBlock> ListGalleryRenditions(
            [Description("Search terms to filter available renditions. Optional when renditionIds are provided.")] string searchTerms = null,
            [Description("Rendition IDs to resolve to URLs and names, e.g. the renditionId values of gallery images. Use this instead of search terms when the IDs are already known.")] List<int> renditionIds = null
            )
        {
            if (_httpContextAccessor.HttpContext.User.Identity.IsAuthenticated)
            {
                var hasRenditionIds = renditionIds != null && renditionIds.Any(id => id > 0);
                if (!hasRenditionIds && string.IsNullOrWhiteSpace(searchTerms))
                {
                    return GetTextContentBlockForErrorResult("Provide searchTerms or renditionIds.");
                }

                var principal = _httpContextAccessor.HttpContext.User;
                var authHeader = _httpContextAccessor.HttpContext.Request.Headers.Authorization.FirstOrDefault();

                var client = _httpClientFactory.CreateClient("AmaisApi");
                client.BaseAddress = new Uri(principal.FindFirst("aud")?.Value);
                client.DefaultRequestHeaders.Add("Authorization", authHeader);
                client.DefaultRequestHeaders.Add("User-Agent", Environment.GetEnvironmentVariable("USER_AGENT_HEADER"));

                var galleryFeatureApi = new GalleryFeatureApi(client, "Etc/UTC");

                var result = await galleryFeatureApi.ListAvailableGalleryFeatureRenditionsAsync(searchTerms, renditionIds);
                if (result != null && !result.IsError)
                {
                    if (result.Data?.NotFoundRenditionIds?.Count > 0)
                    {
                        result.Data.Message = $"These rendition IDs could not be resolved: {string.Join(", ", result.Data.NotFoundRenditionIds)}. "
                            + "Do not guess their URLs; ask the user for a search term, image name or URL instead.";
                    }

                    return new TextContentBlock()
                    {
                        Text = JsonConvert.SerializeObject(result.Data),
                        Meta = new System.Text.Json.Nodes.JsonObject
                        {
                            [CommonMetadataConsts.Timestamp] = _dateTimeProvider.DateTimeUtcNow()
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
