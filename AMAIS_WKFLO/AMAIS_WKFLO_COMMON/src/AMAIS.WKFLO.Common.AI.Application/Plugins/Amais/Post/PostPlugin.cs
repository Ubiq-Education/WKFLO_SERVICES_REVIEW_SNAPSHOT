using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Post
{
    public class PostPlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext, IBlobStorageProvider blobStorageProvider)
        : BaseAmaisPlugin("AMAIS MCP Post plugin", mcpService, dateTimeProvider, userContext, chatContext)
    {
        private readonly IBlobStorageProvider _blobStorageProvider = blobStorageProvider;

        private const string CreatePostToolName = "create_post";
        private const string ClonePostToolName = "clone_post";
        private const string UpdatePostToolName = "update_post";
        private const string UpdatePostImageByImageToolName = "update_post_image";
        private const string UpdatePostImageByRenditionIdToolName = "update_post_image_by_id";
        private const string ListAvailablePostImageNamesToolName = "list_all_post_images";

        [KernelFunction(CreatePostToolName)]
        [Description("Create a new post with a required name. Returns the post alias (unique identifier) and name.")]
        public async Task<List<PluginResultItem>> CreatePost([Description("Name for the new post.")] string postName)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, CreatePostToolName, new Dictionary<string, object>()
                    {
                        { "postName", postName }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error creating the new post."
             );
        }

        [KernelFunction(ClonePostToolName)]
        [Description("Clone an existing post by its alias to create a duplicate with a new name. Before cloning, make sure the user has chosen BOTH clone options (clearSeoAndSocialMetadata and versionUnsharedFeatures). If the user has not said what they want for either option, ask in one short question and do not assume defaults; if the user says to use the defaults or just clone it, pass false for any option they have not chosen and keep any option they already chose.")]
        public async Task<List<PluginResultItem>> ClonePost(
            [Description("Post alias to be cloned.")] string postAliasToClone,
            [Description("New name for the new post.")] string newPostName,
            [Description("If true, clears the meta title/description/keywords, the settings description/image and the OpenGraph (social) title/description/image on the cloned post. By default (false) the Settings, SEO and Social content is copied to the clone. Only set this when the user has said what they want; otherwise omit it. If the user says to use the defaults or just clone it, pass false.")] bool? clearSeoAndSocialMetadata = null,
            [Description("If true, features used only on the source post get their own independent versions on the clone; features already shared with other pages or layouts stay shared. By default (false) all features stay shared (linked) between the source post and the clone. Only set this when the user has said what they want; otherwise omit it. If the user says to use the defaults or just clone it, pass false.")] bool? versionUnsharedFeatures = null)
        {
            var missingOptions = new List<string>();
            if (clearSeoAndSocialMetadata is null)
            {
                missingOptions.Add("clearSeoAndSocialMetadata (whether to clear the Settings, SEO and Social metadata on the clone; by default that content is copied)");
            }
            if (versionUnsharedFeatures is null)
            {
                missingOptions.Add("versionUnsharedFeatures (whether features used only on the source post get their own independent versions; by default all features stay shared between the source post and the clone)");
            }

            if (missingOptions.Count > 0)
            {
                return
                [
                    new PluginResultItem
                    {
                        Message = "The post was NOT cloned yet because the user has not chosen these clone options: " + string.Join("; ", missingOptions) +
                            ". Ask the user about the missing option(s) in one short question, then call clone_post again with both values, keeping any option the user already chose. If the user says to use the defaults or just clone it, pass false only for the option(s) they have not chosen.",
                        Metadata = new Dictionary<string, object> { { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() } }
                    }
                ];
            }
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ClonePostToolName, new Dictionary<string, object>()
                    {
                        { "postAliasToClone", postAliasToClone },
                        { "newPostName", newPostName },
                        { "clearSeoAndSocialMetadata", clearSeoAndSocialMetadata.Value },
                        { "versionUnsharedFeatures", versionUnsharedFeatures.Value }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error cloning the post."
             );
        }

        [KernelFunction(UpdatePostToolName)]
        [Description("Update post metadata and SEO settings. Accepts post alias (required) and optional parameters: post name, menu name, meta title, meta description, OG meta title, OG meta description. Only provided fields will be updated.")]
        public async Task<List<PluginResultItem>> UpdatePost(
            [Description("Post alias to be updated.")] string postAliasToUpdate,
            [Description("New post name for the post to be updated.")] string newPostName = null,
            [Description("New menu name for the post to be updated.")] string newMenuName = null,
            [Description("New meta title for the post to be updated.")] string newMetaTitle = null,
            [Description("New meta description for the post to be updated.")] string newMetaDescription = null,
            [Description("New OG meta title for the post to be updated.")] string newOGMetaTitle = null,
            [Description("New OG meta description for the post to be updated.")] string newOGMetaDescription = null
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePostToolName, new Dictionary<string, object>()
                    {
                        { "postAliasToUpdate", postAliasToUpdate },
                        { "newPostName", newPostName },
                        { "newMenuName", newMenuName },
                        { "newMetaTitle", newMetaTitle },
                        { "newMetaDescription", newMetaDescription },
                        { "newOGMetaTitle", newOGMetaTitle },
                        { "newOGMetaDescription", newOGMetaDescription }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the post."
             );
        }

        [KernelFunction(ListAvailablePostImageNamesToolName)]
        [Description("Search and retrieve all available post images with their metadata. Accepts search terms to filter images by name or other attributes. Returns image details including file names, last updated dates, and rendition IDs.")]
        public async Task<List<PluginResultItem>> ListAvailablePostImageNames(
            [Description("Search terms to filter available post images. Required parameter. Use 'image' as a default value to filter only images.")] string searchTerms
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ListAvailablePostImageNamesToolName, new Dictionary<string, object>()
                    {
                        {  "searchTerms", searchTerms }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error getting the available post images."
             );
        }

        [KernelFunction(UpdatePostImageByImageToolName)]
        [Description("Update a post's featured/main image by uploading a new image file. Requires post alias, file name, and file MIME type (e.g., image/jpeg, image/png).")]
        public async Task<List<PluginResultItem>> UpdatePostImageByImage(
                [Description("Post alias to be updated.")] string postAliasToUpdate,
                [Description("New image file name for the post to be updated.")] string fileName,
                [Description("New image file mime type for the post to be updated.")] string fileMimeType
            )
        {
            return await RunAsync(
                async () =>
                {
                    var imageContent = _chatContext.CurrentChatHistory
                        .Where(m =>
                            m.Items.OfType<ImageContent>().Any() &&
                            m.Metadata.ContainsKey(CommonMetadataConsts.FileName) && m.Metadata[CommonMetadataConsts.FileName].ToString() == fileName &&
                            m.Metadata.ContainsKey(CommonMetadataConsts.FileType) && m.Metadata[CommonMetadataConsts.FileType].ToString() == fileMimeType
                        )
                        .SelectMany(m => m.Items.OfType<ImageContent>()).LastOrDefault();

                    byte[] imageBytes = null;

                    // If imageContent has Data, use it directly
                    if (imageContent?.Data != null)
                    {
                        imageBytes = imageContent?.Data?.ToArray();
                    }
                    // If imageContent has Uri but no Data, download from blob storage
                    else if (imageContent?.Uri != null && imageContent.Uri.AbsolutePath.Contains("/chat-images/"))
                    {
                        var blobPath = string.Concat(imageContent.Uri.Segments.Skip(2).Select(Uri.UnescapeDataString));
                        var stream = await _blobStorageProvider.DownloadAsync("chat-images", blobPath);
                        if (stream != null)
                        {
                            using var memoryStream = new MemoryStream();
                            await stream.CopyToAsync(memoryStream);
                            imageBytes = memoryStream.ToArray();
                        }
                    }

                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePostImageByImageToolName, new Dictionary<string, object>()
                    {
                        { "postAliasToUpdate", postAliasToUpdate },
                        { "newImageBytes", imageBytes },
                        { "newImageName", fileName },
                        { "newImageMimeType", fileMimeType }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the post image."
             );
        }

        [KernelFunction(UpdatePostImageByRenditionIdToolName)]
        [Description("Update a post's featured/main image by assigning an existing rendition ID. Useful for reusing images already in the system. Requires post alias and valid rendition ID.")]
        public async Task<List<PluginResultItem>> UpdatePostImageByRenditionId(
            [Description("Post alias to be updated.")] string postAliasToUpdate,
            [Description("Existing rendition id for post image to be updated.")] int renditionId)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePostImageByRenditionIdToolName, new Dictionary<string, object>()
                        {
                            { "postAliasToUpdate", postAliasToUpdate },
                            { "renditionId", renditionId }
                        }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the post image."
             );
        }
    }
}
