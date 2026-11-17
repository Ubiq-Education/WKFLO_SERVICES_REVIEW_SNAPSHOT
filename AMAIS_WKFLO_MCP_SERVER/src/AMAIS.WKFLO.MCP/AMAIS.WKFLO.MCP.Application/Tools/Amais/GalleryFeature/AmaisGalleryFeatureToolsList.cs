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

        [McpServerTool, Description("Search All Gallery Features with optional filtering by search terms. Returns gallery metadata including name, ID, tags, and associated images/renditions. Gallery images include renditionId and, when the site provides them, url and name. If an image has no url, call list_gallery_renditions with its renditionIds to resolve the URLs.")]
        public async Task<TextContentBlock> ListGalleryFeatures(
            [Description("Search terms to filter available galleries.")] List<string> searchTerms
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

                var result = await galleryFeatureApi.ListGalleryFeaturesAsync(searchTerms);
                if (result != null && !result.IsError)
                {
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
