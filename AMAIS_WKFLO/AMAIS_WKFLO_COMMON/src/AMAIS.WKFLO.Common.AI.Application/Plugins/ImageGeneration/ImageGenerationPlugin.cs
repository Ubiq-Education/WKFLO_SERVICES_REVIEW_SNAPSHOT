using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration
{
    public sealed class ImageGenerationPlugin
    {
        private readonly IImageEditService _imageEditService;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly TenantContext _tenantContext;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger _logger;
        private readonly MD.Domain.Entities.Tool _tool;
        private readonly string _provider;
        private readonly string _model;
        private readonly string _agentType;

        internal ImageGenerationPlugin(MD.Domain.Entities.Tool tool, IImageEditService imageEditService,
            IChatNotificationService notificationService, ChatContext chatContext, TenantContext tenantContext, IBlobStorageProvider blobStorageProvider,
            IWkfloMediator mediator, IDateTimeProvider dateTimeProvider, ILogger logger, string provider, string model, string agentType)
        {
            _tool = tool;
            _imageEditService = imageEditService;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _tenantContext = tenantContext;
            _blobStorageProvider = blobStorageProvider;
            _dateTimeProvider = dateTimeProvider;
            _mediator = mediator;
            _logger = logger;
            _provider = provider;
            _model = model;
            _agentType = agentType;
        }

        public static KernelPlugin BuildKernelPlugin(
            MD.Domain.Entities.Tool tool,
            IChatNotificationService notificationService,
            ChatContext chatContext,
            TenantContext tenantContext,
            IBlobStorageProvider blobStorageProvider,
            IWkfloMediator mediator,
            IDateTimeProvider dateTimeProvider,
            ILogger logger,
            string pluginName,
            string agentType)
        {
            var config = JsonConvert.DeserializeObject<ImageGenerationToolConfig>(tool.Config)
                ?? throw new InvalidOperationException($"Tool '{tool.Name}' has invalid ImageGenerationToolConfig.");

            var imageEditService = ImageEditServiceFactory.Create(config);
            var provider = config.Provider ?? string.Empty;
            var model = config.Model ?? string.Empty;
            var instance = new ImageGenerationPlugin(tool, imageEditService, notificationService, chatContext, tenantContext, blobStorageProvider, mediator, dateTimeProvider, logger, provider, model, agentType);
            return KernelPluginFactory.CreateFromObject(instance, pluginName);
        }

        [KernelFunction("edit_image")]
        [Description("Edits or transforms an image based on a text instruction. Use this when the user wants to modify, transform, or apply a style to an image they have uploaded. By default the plugin uses the most recently uploaded image in the conversation; pass fileNameJson to target a specific earlier image instead. The user may also ask to override the image quality or aspect ratio for this edit only.")]
        public async Task<string> EditImageAsync(
            [Description("A clear description of the desired edit or transformation to apply to the image.")] string instruction,
            [Description("Optional. Overrides the tool's configured image quality for this edit only. Allowed values: 'low', 'medium', 'high'. Omit to use the tool's default.")] string quality = null,
            [Description("Optional. Omit this unless the user explicitly asks for a different shape: when omitted, the tool's configured aspect ratio is used, or the aspect ratio of the input image if none is configured. Overrides the output aspect ratio for this edit only, e.g. '1:1', '16:9', '9:16', '3:4', '2:3', '3:2', '4:5', '21:9'. Google honours the ratio; Azure maps it to the closest supported size (square, landscape or portrait).")] string aspectRatio = null,
            [Description("Optional. JSON object identifying which previously uploaded image to edit, shaped {\"fileName\": <string>, \"fileMimeType\": <string>} — both must match exactly what was shown earlier in this conversation when the file was uploaded/generated. Omit to edit the most recently uploaded image.")] string fileNameJson = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId, _tool.Name, "running", "Editing image...", statusCode: 200);

                var (resolvedUrl, resolvedBytes) = ResolveImage(fileNameJson);
                var result = resolvedUrl != null
                    ? await _imageEditService.EditImageAsync(resolvedUrl, instruction, quality, aspectRatio, cancellationToken)
                    : await _imageEditService.EditImageAsync(resolvedBytes, instruction, quality, aspectRatio, cancellationToken);

                // US 7974 / Task 7979: stored under the tenant's prefix; the model and the chat still see the
                // tenant-less "edited/{id}.{ext}" file name, as before.
                var imageId = Guid.NewGuid();
                var fileName = ChatImageBlobNames.EditedImageFileName(imageId, result.Extension);
                var blobName = ChatImageBlobNames.ForEditedImage(_tenantContext?.TenantGuid, imageId, result.Extension);
                if (blobName == fileName)
                    _logger.LogWarning("ImageGenerationPlugin: tenant unknown for session {SessionId}; storing edited image under the legacy name {BlobName}", _chatContext.SessionId, blobName);

                using var stream = new MemoryStream(result.ImageBytes);
                var blobUrl = await _blobStorageProvider.UploadAsync(ChatImageBlobNames.ContainerName, blobName, stream, result.MimeType, cancellationToken);
                var sasUrl = await _blobStorageProvider.GenerateSasUrlAsync(new Uri(blobUrl), TimeSpan.FromHours(1));

                // Message Id
                var messageId = Guid.NewGuid().ToString();

                await _mediator.Publish(new AgentTokensUsedDomainEvent(new AgentAnswer
                {
                    MessageId = messageId,
                    Message = "AI generated image. User message: " + instruction,
                    TokenUsage = result.TokenUsage,
                    Provider = _provider,
                    Model = _model,
                    AgentType = _agentType,
                    // WI 7303: AIModelId intentionally left null - this path only has the plain _provider/_model
                    // strings parsed from ImageGenerationToolConfig, not a loaded AIModel entity to bind credits to.
                }), cancellationToken);

                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId, _tool.Name, "completed", "Image edited successfully.", statusCode: 200);

                _chatContext.AssistantPostMessagesToBeProcessed.Add(BuildImageMessage(sasUrl, messageId, fileName, result.MimeType));

                // The edited image reaches the chat as its own post message (BuildImageMessage), so the model
                // must not repeat it - but it must still say something: told only "don't include the image",
                // it often ends the turn with an empty reply that renders as a blank "<p></p>" bubble.
                return "Image editing completed successfully. The edited image is shown to the user separately, so do not include it, its file name or its URL in your reply. Reply with one short sentence confirming the edit is done.";
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // The caller stopped the turn: not a tool failure, and nothing to report. Any exception type, because Flurl
                // wraps a cancellation in a FlurlHttpException (inner TaskCanceledException) instead of throwing OperationCanceledException.
                throw;
            }
            catch (ImageEditInputException ex)
            {
                // Our own input validation (no image / bad file reference): the message is written for the user and
                // actionable, so it is passed on as is.
                _logger.LogWarning(ex, "ImageGenerationPlugin input error for tool {ToolName} on session {SessionId}", _tool.Name, _chatContext.SessionId);

                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId, _tool.Name, "failed", "Image editing failed.", statusCode: 500);

                return $"<p>Image editing failed: {System.Net.WebUtility.HtmlEncode(ex.Message)}</p>";
            }
            catch (Exception ex)
            {
                var info = await ImageEditErrorClassifier.DescribeAsync(ex);

                // Single structured log; the SK invocation filter never sees this failure because the plugin
                // swallows it and returns text (Bug 8294 only logs tool exceptions that propagate).
                var level = info.Kind is ImageEditFailureKind.QuotaExceeded or ImageEditFailureKind.NotConfigured or ImageEditFailureKind.Other
                    ? LogLevel.Error
                    : LogLevel.Warning;
                // The exception (and its stack) is only attached for unclassified failures; for classified provider failures
                // {ProviderMessage} already carries the same text, so passing ex too would log it twice.
                _logger.Log(level, info.Kind == ImageEditFailureKind.Other ? ex : null,
                    "ImageGenerationPlugin failed for tool {ToolName} on session {SessionId}: provider {Provider}, model {Model}, kind {FailureKind}, status code {ProviderStatusCode}, status {ProviderStatus}, message {ProviderMessage}, block reason {BlockReason}, finish reasons {FinishReasons}",
                    _tool.Name, _chatContext.SessionId, _provider, _model, info.Kind, info.StatusCode, info.Status, info.Message, info.BlockReason, info.FinishReasons);

                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId, _tool.Name, "failed", $"Image editing failed: {ImageEditFailureMessages.Short(info.Kind)}", statusCode: 500);

                return $"<p>Image editing failed: {ImageEditFailureMessages.Friendly(info.Kind)}</p> Explain this to the user in your own words. Do not mention error codes, provider names or technical details.";
            }
        }

        private ChatMessageContent BuildImageMessage(string sasUrl, string messageId, string fileName, string mimeType)
        {
            var metadataImage = new Dictionary<string, object>
            {
                { CommonMetadataConsts.FileName, fileName },
                { CommonMetadataConsts.FileType, mimeType },
                { CommonMetadataConsts.Timestamp,  _dateTimeProvider.DateTimeOffsetUtcNow() },
                { CommonMetadataConsts.MessageId, messageId },
                { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Metadata },
                { CommonMetadataConsts.RenderAsSystem, true }
            };

            var messageContentImage = new ChatMessageContentItemCollection
            {
                new TextContent(ChatImageLabel.ForReturnedImage(fileName, mimeType)),
                new ImageContent()
                {
                    Uri = new Uri(sasUrl)
                }
            };

            // AuthorRole.User, not Assistant: Anthropic separately rejects an 'image' content block
            // inside an assistant turn outright ("'image' blocks are not permitted within assistant
            // turns"), regardless of position. This message isn't part of the edit_image tool call's
            // own tool_use/tool_result exchange (EditImageAsync's plain string return already gets
            // paired with its matching tool_use_id automatically by SK) - it's a separate, later-
            // appended message, so there's no tool_use_id to attach a tool_result to here. A plain
            // User-role image block has no positional/pairing constraint at all, unlike System or
            // Assistant. RenderAsSystem keeps the Metadata-channel Info icon rendering; the image
            // itself renders via message.fileUrl - see chat-messages.tsx, which may need a matching
            // wkflo_ui update now that Type flows through as "user" instead of "assistant"
            // (ProcessAssistantPostMessagesAsync maps ChatMessageContent.Role straight to ChatMessage.Type).
            return new ChatMessageContent(AuthorRole.User, messageContentImage, metadata: metadataImage);
        }

        private (string url, byte[] bytes) ResolveImage(string fileNameJson)
        {
            if (_chatContext.CurrentChatHistory == null)
                throw new ImageEditInputException("No image found in conversation history. Please upload an image first.");

            if (!string.IsNullOrWhiteSpace(fileNameJson))
                return ResolveTargetedImage(fileNameJson);

            for (var i = _chatContext.CurrentChatHistory.Count - 1; i >= 0; i--)
            {
                var imageContent = _chatContext.CurrentChatHistory[i].Items?.OfType<ImageContent>().FirstOrDefault();
                if (imageContent == null) continue;

                if (imageContent.Uri != null)
                    return (imageContent.Uri.ToString(), null);

                if (imageContent.Data.HasValue && imageContent.Data.Value.Length > 0)
                    return (null, imageContent.Data.Value.ToArray());
            }

            throw new ImageEditInputException("No image found in conversation history. Please upload an image first.");
        }

        // Same lookup convention as AgentEventPublisherPlugin.ResolveImageAttachmentAsync/
        // PostPlugin.UpdatePostImageByImage: match on the message-level FileName/FileType metadata that
        // SemanticKernelAgentServiceRun stamps onto every uploaded image message.
        private (string url, byte[] bytes) ResolveTargetedImage(string fileNameJson)
        {
            AgentEventFileReference fileRef;
            try
            {
                fileRef = JsonConvert.DeserializeObject<AgentEventFileReference>(fileNameJson);
            }
            catch (JsonException ex)
            {
                throw new ImageEditInputException("The provided file reference was not valid JSON.", ex);
            }

            if (fileRef == null || string.IsNullOrWhiteSpace(fileRef.FileName) || string.IsNullOrWhiteSpace(fileRef.FileMimeType))
                throw new ImageEditInputException("The provided file reference must include both fileName and fileMimeType.");

            var imageContent = _chatContext.CurrentChatHistory
                .Where(m =>
                    m.Items?.OfType<ImageContent>().Any() == true &&
                    m.Metadata != null &&
                    m.Metadata.ContainsKey(CommonMetadataConsts.FileName) && m.Metadata[CommonMetadataConsts.FileName].ToString() == fileRef.FileName &&
                    m.Metadata.ContainsKey(CommonMetadataConsts.FileType) && m.Metadata[CommonMetadataConsts.FileType].ToString() == fileRef.FileMimeType)
                .SelectMany(m => m.Items.OfType<ImageContent>())
                .LastOrDefault();

            if (imageContent != null)
            {
                if (imageContent.Uri != null)
                    return (imageContent.Uri.ToString(), null);

                if (imageContent.Data.HasValue && imageContent.Data.Value.Length > 0)
                    return (null, imageContent.Data.Value.ToArray());
            }

            throw new ImageEditInputException($"No image found matching fileName '{fileRef.FileName}' and fileMimeType '{fileRef.FileMimeType}'.");
        }
    }
}
