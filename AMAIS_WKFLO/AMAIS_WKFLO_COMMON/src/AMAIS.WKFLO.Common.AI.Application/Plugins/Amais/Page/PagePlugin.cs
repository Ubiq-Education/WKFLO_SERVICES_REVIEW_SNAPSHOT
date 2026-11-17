using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Page
{
    public class PagePlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext, IBlobStorageProvider blobStorageProvider)
        : BaseAmaisPlugin("AMAIS MCP Page plugin", mcpService, dateTimeProvider, userContext, chatContext)
    {
        private readonly IBlobStorageProvider _blobStorageProvider = blobStorageProvider;

        private const string CreatePageToolName = "create_page";
        private const string ClonePageToolName = "clone_page";
        private const string UpdatePageToolName = "update_page";
        private const string UpdatePageImageByImageToolName = "update_page_image";
        private const string UpdatePageImageByRenditionIdToolName = "update_page_image_by_id";
        internal const string GetPageDataToolName = "get_page_data";
        private const string ListAvailablePageImageNamesToolName = "list_all_page_images";
        private const string GetPageTemplatesToolName = "get_page_templates";

        [KernelFunction(CreatePageToolName)]
        [Description("Create a new page with a required name and an optional template. Returns the page alias (unique identifier) and name.")]
        public async Task<List<PluginResultItem>> CreatePage(
            [Description("Name for the new page.")] string pageName,
            [Description("Template to use for the new page.")] string template = null)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, CreatePageToolName, new Dictionary<string, object>()
                    {
                        { "pageName", pageName },
                        { "template", template }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error creating the new page."
             );
        }

        [KernelFunction(ClonePageToolName)]
        [Description("Clone an existing page by its alias to create a duplicate with a new name. Before cloning, make sure the user has chosen BOTH clone options (clearSeoAndSocialMetadata and versionUnsharedFeatures). If the user has not said what they want for either option, ask in one short question and do not assume defaults; if the user says to use the defaults or just clone it, pass false for any option they have not chosen and keep any option they already chose.")]
        public async Task<List<PluginResultItem>> ClonePage(
            [Description("Page alias to be cloned.")] string pageAliasToClone,
            [Description("New name for the new page.")] string newPageName,
            [Description("If true, clears the meta title/description/keywords, the settings description/image and the OpenGraph (social) title/description/image on the cloned page. By default (false) the Settings, SEO and Social content is copied to the clone. Only set this when the user has said what they want; otherwise omit it. If the user says to use the defaults or just clone it, pass false.")] bool? clearSeoAndSocialMetadata = null,
            [Description("If true, features used only on the source page get their own independent versions on the clone; features already shared with other pages or layouts stay shared. By default (false) all features stay shared (linked) between the source page and the clone. Only set this when the user has said what they want; otherwise omit it. If the user says to use the defaults or just clone it, pass false.")] bool? versionUnsharedFeatures = null)
        {
            var missingOptions = new List<string>();
            if (clearSeoAndSocialMetadata is null)
            {
                missingOptions.Add("clearSeoAndSocialMetadata (whether to clear the Settings, SEO and Social metadata on the clone; by default that content is copied)");
            }
            if (versionUnsharedFeatures is null)
            {
                missingOptions.Add("versionUnsharedFeatures (whether features used only on the source page get their own independent versions; by default all features stay shared between the source page and the clone)");
            }

            if (missingOptions.Count > 0)
            {
                return
                [
                    new PluginResultItem
                    {
                        Message = "The page was NOT cloned yet because the user has not chosen these clone options: " + string.Join("; ", missingOptions) +
                            ". Ask the user about the missing option(s) in one short question, then call clone_page again with both values, keeping any option the user already chose. If the user says to use the defaults or just clone it, pass false only for the option(s) they have not chosen.",
                        Metadata = new Dictionary<string, object> { { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() } }
                    }
                ];
            }
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ClonePageToolName, new Dictionary<string, object>()
                    {
                        { "pageAliasToClone", pageAliasToClone },
                        { "newPageName", newPageName },
                        { "clearSeoAndSocialMetadata", clearSeoAndSocialMetadata.Value },
                        { "versionUnsharedFeatures", versionUnsharedFeatures.Value }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error cloning the page."
             );
        }

        [KernelFunction(UpdatePageToolName)]
        [Description("Update page metadata and SEO settings. Accepts page alias (required) and optional parameters: page name, menu name, meta title, meta description, OG meta title, OG meta description. Only provided fields will be updated.")]
        public async Task<List<PluginResultItem>> UpdatePage(
            [Description("Page alias to be updated.")] string pageAliasToUpdate,
            [Description("New page name for the page to be updated.")] string newPageName = null,
            [Description("New menu name for the page to be updated.")] string newMenuName = null,
            [Description("New meta title for the page to be updated.")] string newMetaTitle = null,
            [Description("New meta description for the page to be updated.")] string newMetaDescription = null,
            [Description("New OG meta title for the page to be updated.")] string newOGMetaTitle = null,
            [Description("New OG meta description for the page to be updated.")] string newOGMetaDescription = null
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePageToolName, new Dictionary<string, object>()
                    {
                        { "pageAliasToUpdate", pageAliasToUpdate },
                        { "newPageName", newPageName },
                        { "newMenuName", newMenuName },
                        { "newMetaTitle", newMetaTitle },
                        { "newMetaDescription", newMetaDescription },
                        { "newOGMetaTitle", newOGMetaTitle },
                        { "newOGMetaDescription", newOGMetaDescription }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the page."
             );
        }

        [KernelFunction(GetPageDataToolName)]
        [Description("Retrieve the data for a specific page by its alias. Returns page name, menu name, alias, meta title, meta description, OG meta title, OG meta description, and URL.")]
        public async Task<List<PluginResultItem>> GetPageData(
            [Description("Alias (unique identifier) of the page to retrieve.")] string pageAlias)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, GetPageDataToolName, new Dictionary<string, object>()
                    {
                        { "pageAlias", pageAlias }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the page data."
             );
        }

        [KernelFunction(ListAvailablePageImageNamesToolName)]
        [Description("Search and retrieve all available page images with their metadata. Accepts search terms to filter images by name or other attributes. Returns image details including file names, last updated dates, and rendition IDs.")]
        public async Task<List<PluginResultItem>> ListAvailablePageImageNames(
            [Description("Search terms to filter available page images. Required parameter. Use 'image' as a default value to filter only images.")] string searchTerms
            )
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, ListAvailablePageImageNamesToolName, new Dictionary<string, object>()
                    {
                        {  "searchTerms", searchTerms }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error getting the available page images."
             );
        }

        [KernelFunction(UpdatePageImageByImageToolName)]
        [Description("Update a page's featured/main image by uploading a new image file. Requires page alias, file name, and file MIME type (e.g., image/jpeg, image/png).")]
        public async Task<List<PluginResultItem>> UpdatePageImageByImage(
                [Description("Page alias to be updated.")] string pageAliasToUpdate,
                [Description("New image file name for the page to be updated.")] string fileName,
                [Description("New image file mime type for the page to be updated.")] string fileMimeType
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

                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePageImageByImageToolName, new Dictionary<string, object>()
                    {
                        { "pageAliasToUpdate", pageAliasToUpdate },
                        { "newImageBytes", imageBytes },
                        { "newImageName", fileName },
                        { "newImageMimeType", fileMimeType }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the page image."
             );
        }

        [KernelFunction(UpdatePageImageByRenditionIdToolName)]
        [Description("Update a page's featured/main image by assigning an existing rendition ID. Useful for reusing images already in the system. Requires page alias and valid rendition ID.")]
        public async Task<List<PluginResultItem>> UpdatePageImageByRenditionId(
            [Description("Page alias to be updated.")] string pageAliasToUpdate,
            [Description("Existing rendition id for page image to be updated.")] int renditionId)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, UpdatePageImageByRenditionIdToolName, new Dictionary<string, object>()
                        {
                            { "pageAliasToUpdate", pageAliasToUpdate },
                            { "renditionId", renditionId }
                        }, CancellationToken.None);
                },
                GenerateMetadataMessageWithURL,
                "There was an error updating the page image."
             );
        }

        [KernelFunction(GetPageTemplatesToolName)]
        [Description("Retrieve all available page templates ordered by popularity (most used first). Returns template names and their page count.")]
        public async Task<List<PluginResultItem>> GetPageTemplates()
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, GetPageTemplatesToolName, new Dictionary<string, object>(), CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the available page templates."
             );
        }
    }
}
