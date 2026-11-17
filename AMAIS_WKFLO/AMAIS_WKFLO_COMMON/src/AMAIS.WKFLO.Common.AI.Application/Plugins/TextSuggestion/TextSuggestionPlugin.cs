using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.TextSuggestion
{
    public class TextSuggestionPlugin
    {
        private readonly Tool _tool;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger _logger;

        public TextSuggestionPlugin(Tool tool, IChatNotificationService notificationService, ChatContext chatContext, IDateTimeProvider dateTimeProvider, ILogger logger)
        {
            _tool = tool;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        [Description("Suggest an edit to a text feature the user has selected on the page")]
        public async Task<string> SuggestTextEditAsync(
            [Description("ID of the text feature to suggest the edit for. Use the ID of the feature currently selected by the user, typically already retrieved via get_text_feature.")] int featureId,
            [Description("The suggested text content, formatted the same way as the feature's existing content (e.g. HTML), that the user can choose to add to the top, add to the bottom, or use to replace the feature's current text.")] string suggestedText)
        {
            try
            {
                var update = new TextSuggestionUpdate
                {
                    FeatureId = featureId,
                    FeatureName = _chatContext.FeatureName ?? string.Empty,
                    SuggestedText = suggestedText ?? string.Empty
                };

                // Routed through the normal chat pipeline (AssistantPostMessagesToBeProcessed) instead of
                // a dedicated SignalR side-channel, so it's both pushed live AND persisted/reconstructed
                // from ChatHistory on reload — see SemanticKernelAgentService.ProcessAssistantPostMessagesAsync
                // and ImageGenerationPlugin.BuildImageMessage for the same pattern.
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() },
                    { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Forms },
                    { CommonMetadataConsts.FormType, "TextSuggestion" },
                    { CommonMetadataConsts.Data, JsonConvert.SerializeObject(update) }
                };
                // Anthropic's API rejects empty text content blocks ("system: text content blocks
                // must be non-empty") when this message is later replayed as conversation context
                // on the next turn — so Content must never be empty. It also doubles as MAISY's own
                // memory of what it suggested (Metadata is never sent to the LLM, only Content/Items are).
                var messageText = !string.IsNullOrWhiteSpace(update.SuggestedText)
                    ? update.SuggestedText
                    : "Text suggestion for the selected feature.";
                // AuthorRole.Assistant, not System: see BaseAmaisPlugin.GenerateMetadataMessageWithURL
                // for why (ProcessAssistantPostMessagesAsync appends this after the assistant's
                // plain-text response, where Anthropic rejects a 'system' role message). No
                // RenderAsSystem flag needed here - the chat UI renders this via message.textSuggestion
                // (hydrated purely from metadata.formType/data, never from role), not the
                // isSystemStyled path used by BaseAmaisPlugin/SemanticKernelAgentServiceRun.
                _chatContext.AssistantPostMessagesToBeProcessed.Add(new ChatMessageContent(AuthorRole.Assistant, messageText, metadata: metadata));

                return "A suggested text edit has been displayed to the user with options to add it to the top, add it to the bottom, or replace all of the feature's current text. Do not repeat the suggested text in your reply — simply let the user know the suggestion is ready for them to review and apply.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing TextSuggestionPlugin for tool {ToolName}", _tool.Name);
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    "Loading text suggestion failed...",
                    statusCode: 500
                );

                return string.Empty;
            }
        }
    }
}
