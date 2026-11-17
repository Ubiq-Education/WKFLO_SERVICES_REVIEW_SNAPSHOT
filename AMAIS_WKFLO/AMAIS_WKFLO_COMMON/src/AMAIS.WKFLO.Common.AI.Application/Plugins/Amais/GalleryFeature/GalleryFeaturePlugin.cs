using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.GalleryFeature
{
    public class GalleryFeaturePlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext, IBlobStorageProvider blobStorageProvider)
        : BaseAmaisPlugin("AMAIS MCP GalleryFeature plugin", mcpService, dateTimeProvider, userContext, chatContext)
    {
        private readonly IBlobStorageProvider _blobStorageProvider = blobStorageProvider;

        private const string CreateGalleryFeatureToolName = "create_gallery_feature";
        private const string UpdateGalleryFeatureToolName = "update_gallery_feature";
        private const string ListGalleryRenditionsToolName = "list_gallery_renditions";
        private const string ListGalleryFeaturesToolName = "list_gallery_features";
        internal const string GetGalleryFeatureToolName = "get_gallery_feature";

        [KernelFunction(CreateGalleryFeatureToolName)]
        [Description("Create a new Gallery Feature with a required name and optional tags. Returns the gallery ID needed for updating the gallery with images.")]
        public async Task<List<PluginResultItem>> CreateGalleryFeature(
            [Description("Name for the new gallery feature.")] string name,
            [Description("Tags for the new gallery feature.")] List<string> tags = null
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, CreateGalleryFeatureToolName, new Dictionary<string, object>()
                    {
                        { "name", name },
                        { "tags", tags }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error creating the new gallery feature."
             );
        }

        [KernelFunction(UpdateGalleryFeatureToolName)]
        [Description("Update Gallery Feature by adding new images, rendition IDs, or tags. Requires gallery ID. You can add images by file name/type OR by existing rendition IDs.")]
        public async Task<List<PluginResultItem>> UpdateGalleryFeature(
            [Description(@"Image size strategy, ask for strategy if the gallery has a preset ids greater than 0. Possible values: 0 = Default, 1 = Preset, 2 = Custom.
                           Note: Default option means it will use the image default size.
                           Conditions:
                            1 - If Preset is chosen then Preset ID must be provided.
                            2 - If Custom is chosen then Image Width and Image Height must be provided.")] int imageSizeStrategy,
            [Description("Gallery id to be updated.")] int galleryId = 0,
            [Description("Gallery name to be updated.")] string galleryName = null,
            [Description("New tags to be assigned.")] List<string> tags = null,
            [Description("New images to be added.")] List<GalleryImage> images = null,
            [Description("New rendition ids to be added.")] List<int> renditionIds = null,
            [Description("Preset ID.")] int? presetId = null,
            [Description("Image width.")] int? imageWidth = null,
            [Description("Image height.")] int? imageHeight = null
            )
        {
            if (!string.IsNullOrEmpty(galleryName) && galleryId == 0)
            {
                var items = await ListAllGalleryFeatures([galleryName]);
                items.FirstOrDefault()?.Metadata.TryAdd(CommonMetadataConsts.Message, "I didn't find the gallery in the chat id but AMAIS has all these galleries available.");
                return items;
            }

            var imagesContent = new List<ImageContentData>();

            if (images != null && images.Count > 0)
            {
                imagesContent = [.. images.Select(m =>
                        _chatContext.CurrentChatHistory.Where(m2 =>
                            m2.Items.OfType<ImageContent>().Any() &&
                            m2.Metadata.ContainsKey(CommonMetadataConsts.FileName) && m2.Metadata[CommonMetadataConsts.FileName].ToString() == m.FileName &&
                            m2.Metadata.ContainsKey(CommonMetadataConsts.FileType) && m2.Metadata[CommonMetadataConsts.FileType].ToString() == m.FileMimeType
                        )
                        .Select(m2 => new ImageContentData
                        {
                            NewImageBytes = m2.Items.OfType<ImageContent>().LastOrDefault()?.Data?.ToArray(),
                            NewImageUrl = m2.Items.OfType<ImageContent>().LastOrDefault()?.Uri,
                            NewImageName = m2.Metadata[CommonMetadataConsts.FileName].ToString(),
                            NewImageMimeType = m2.Metadata[CommonMetadataConsts.FileType].ToString()
                        })
                    )
                    .SelectMany(m => m)
                    .Where(m => m?.NewImageBytes != null || m?.NewImageUrl != null)];
            }

            if ((imagesContent == null || !imagesContent.Any()) && (images != null && images.Any()))
            {
                var items = await ListAvailableGalleryFeatureRenditions(string.Join(" ", (images ?? []).Select(m => m.FileName ?? string.Empty)));
                items.FirstOrDefault()?.Metadata.TryAdd(CommonMetadataConsts.Message, "I didn't find the rendions in the chat but AMAIS has all these renditions available. Ask the user if they want to use any of them.");
                return items;
            }

            foreach (var image in imagesContent)
            {
                if (image.NewImageBytes == null && image.NewImageUrl != null)
                {
                    var imageUri = image.NewImageUrl;
                    var blobPath = string.Concat(imageUri.Segments.Skip(2).Select(Uri.UnescapeDataString));
                    var stream = await _blobStorageProvider.DownloadAsync("chat-images", blobPath);

                    if (stream != null)
                    {
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        var base64String = Convert.ToBase64String(memoryStream.ToArray());
                        var dataUri = $"data:{image.NewImageMimeType};base64,{base64String}";

                        var (bytes, mimeType) = dataUri.ParseImageData();
                        image.NewImageBytes = bytes;
                        image.NewImageMimeType = mimeType;
                    }
                }
            }

            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdateGalleryFeatureToolName, new Dictionary<string, object>()
                    {
                        { "id", galleryId },
                        { "name", galleryName },
                        { "tags", tags },
                        { "images", imagesContent },
                        { "renditionIds", renditionIds },
                        { "imageSizeStrategy", imageSizeStrategy },
                        { "presetId", presetId },
                        { "imageWidth", imageWidth },
                        { "imageHeight", imageHeight }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the gallery feature."
             );
        }

        [KernelFunction(ListGalleryFeaturesToolName)]
        [Description("Search All Gallery Features with filtering by search terms. Returns gallery metadata including name, ID, tags, gallery images metadata like renditionId, galleryId, width, height, presetId and, when the site provides them, url and name. If a gallery image has no url, call list_gallery_renditions with its renditionIds to resolve the URLs.")]
        public async Task<List<PluginResultItem>> ListAllGalleryFeatures(
            [Description("Search terms to filter available gallery features. Required parameter.")] List<string> searchTerms
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ListGalleryFeaturesToolName, new Dictionary<string, object>()
                    {
                        {  "searchTerms", searchTerms }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error getting all gallery features."
             );
        }

        [KernelFunction(GetGalleryFeatureToolName)]
        [Description("Retrieve the data for a specific gallery feature by its ID. Returns gallery ID, name, tags, last updated date, and gallery images (renditionId and, when the site provides them, url and name). If a gallery image has no url, call list_gallery_renditions with its renditionIds to resolve the URLs.")]
        public async Task<List<PluginResultItem>> GetGalleryFeature(
            [Description("ID of the gallery feature to retrieve.")] int featureId)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, GetGalleryFeatureToolName, new Dictionary<string, object>()
                    {
                        { "id", featureId }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the gallery feature data."
             );
        }

        [KernelFunction(ListGalleryRenditionsToolName)]
        [Description("Search available renditions (images and videos) across all galleries by search terms, or resolve known rendition IDs (e.g. renditionId values from get_gallery_feature or list_gallery_features) to their URLs. Returns rendition IDs, URLs, names and lastUpdatedDate but does not indicate which gallery each rendition belongs to. When renditionIds are given, IDs that could not be resolved are listed in notFoundRenditionIds; never guess their URLs.")]
        public async Task<List<PluginResultItem>> ListAvailableGalleryFeatureRenditions(
            [Description("Search terms to filter available gallery feature renditions (images and videos). Required unless renditionIds are provided. When no rendition IDs are known, use 'image' as a default value to filter only images.")] string searchTerms = null,
            [Description("Rendition IDs to resolve to URLs and names, e.g. the renditionId values of gallery images. When provided, searchTerms can be omitted.")] List<int> renditionIds = null
            )
        {
            var arguments = new Dictionary<string, object>()
            {
                { "searchTerms", searchTerms }
            };

            // Only send renditionIds when present so keyword-only calls keep the exact previous payload.
            if (renditionIds != null && renditionIds.Count > 0)
            {
                arguments.Add("renditionIds", renditionIds);
            }

            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ListGalleryRenditionsToolName, arguments, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error getting the available gallery feature renditions."
             );
        }
    }
}
