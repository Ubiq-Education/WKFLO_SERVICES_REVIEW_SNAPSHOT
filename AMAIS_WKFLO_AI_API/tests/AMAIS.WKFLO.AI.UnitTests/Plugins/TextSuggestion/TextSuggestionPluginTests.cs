using AMAIS.WKFLO.Common.AI.Application.Plugins.TextSuggestion;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.TextSuggestion
{
    public class TextSuggestionPluginTests
    {
        private readonly Mock<IChatNotificationService> _notificationServiceMock;
        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
        private readonly ChatContext _chatContext;
        private readonly ILogger _logger = NullLogger.Instance;

        private const string SessionId = "session-abc";
        private const string FeatureName = "Homepage Hero";
        private static readonly DateTimeOffset FixedDate = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        // In production this plugin is instantiated with the agent's Integration_Amais Tool
        // (see SemanticKernelExtensions.AddWkflowToolsAsync — it rides the same AreSkillsEnabled
        // gate as get_text_feature), not a dedicated tool type. The plugin itself doesn't read
        // Tool.Config, so the exact tool used here is incidental to what's under test.
        private const string ToolName = "Amais Integration";

        public TextSuggestionPluginTests()
        {
            _notificationServiceMock = new Mock<IChatNotificationService>();
            _notificationServiceMock
                .Setup(x => x.SendToolExecutionUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _dateTimeProviderMock = new Mock<IDateTimeProvider>();
            _dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(FixedDate);

            // FeatureName is populated on ChatContext from the amais-wkflo-feature-name header
            // (see ChatContextResolver) whenever wkflo_ui has a feature selected.
            _chatContext = new ChatContext { SessionId = SessionId, FeatureName = FeatureName };
        }

        [Fact]
        public async Task SuggestTextEditAsync_QueuesAssistantPostMessageWithFormsMetadata()
        {
            // Arrange
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);

            // Act
            await plugin.SuggestTextEditAsync(42, "<p>Suggested copy</p>");

            // Assert — queued onto AssistantPostMessagesToBeProcessed (drained by
            // SemanticKernelAgentService.ProcessAssistantPostMessagesAsync into both the live
            // push AND ChatHistory/persistence), not pushed via a dedicated SignalR side-channel.
            var message = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            Assert.Equal(AuthorRole.Assistant, message.Role);
            // Content must never be empty: Anthropic's API rejects empty text content blocks when
            // this message is replayed as conversation context on the next turn, and Metadata alone
            // (never sent to the LLM) would otherwise leave MAISY with no memory of the suggestion.
            Assert.False(string.IsNullOrWhiteSpace(message.Content));
            Assert.Equal("<p>Suggested copy</p>", message.Content);
            Assert.NotNull(message.Metadata);
            Assert.Equal(ChannelMessageType.Forms, message.Metadata[CommonMetadataConsts.ChannelMessageType]);
            Assert.Equal("TextSuggestion", message.Metadata[CommonMetadataConsts.FormType]);
            Assert.Equal(FixedDate, message.Metadata[CommonMetadataConsts.Timestamp]);
            Assert.False(string.IsNullOrEmpty((string)message.Metadata[CommonMetadataConsts.MessageId]));

            var update = JsonConvert.DeserializeObject<TextSuggestionUpdate>((string)message.Metadata[CommonMetadataConsts.Data]);
            Assert.Equal(42, update.FeatureId);
            Assert.Equal(FeatureName, update.FeatureName);
            Assert.Equal("<p>Suggested copy</p>", update.SuggestedText);
        }

        [Fact]
        public async Task SuggestTextEditAsync_DataJson_UsesCamelCaseKeys_ForFrontendHydration()
        {
            // Arrange — regression guard: the frontend's hydrateFormsMetadata()/TextSuggestionCard
            // expect camelCase keys (featureId/featureName/suggestedText) in the raw JSON string,
            // not the PascalCase JsonConvert.SerializeObject would emit without [JsonProperty]
            // attributes on TextSuggestionUpdate.
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);

            // Act
            await plugin.SuggestTextEditAsync(42, "<p>Suggested copy</p>");

            // Assert
            var message = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            var rawJson = (string)message.Metadata[CommonMetadataConsts.Data];
            Assert.Contains("\"featureId\"", rawJson);
            Assert.Contains("\"featureName\"", rawJson);
            Assert.Contains("\"suggestedText\"", rawJson);
            Assert.DoesNotContain("\"FeatureId\"", rawJson);
            Assert.DoesNotContain("\"FeatureName\"", rawJson);
            Assert.DoesNotContain("\"SuggestedText\"", rawJson);
        }

        [Fact]
        public async Task SuggestTextEditAsync_StampsFeatureNameFromChatContext_NotFromLlmArguments()
        {
            // Arrange — the LLM never supplies a feature name; it's stamped from ChatContext.
            var chatContextWithFeature = new ChatContext { SessionId = SessionId, FeatureName = "Homepage Hero" };
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, chatContextWithFeature, _dateTimeProviderMock.Object, _logger);

            // Act
            await plugin.SuggestTextEditAsync(42, "<p>Suggested copy</p>");

            // Assert
            var message = Assert.Single(chatContextWithFeature.AssistantPostMessagesToBeProcessed);
            var update = JsonConvert.DeserializeObject<TextSuggestionUpdate>((string)message.Metadata[CommonMetadataConsts.Data]);
            Assert.Equal("Homepage Hero", update.FeatureName);
        }

        [Fact]
        public async Task SuggestTextEditAsync_NullFeatureNameOnChatContext_SendsEmptyString()
        {
            // Arrange
            var chatContextWithoutFeature = new ChatContext { SessionId = SessionId, FeatureName = null };
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, chatContextWithoutFeature, _dateTimeProviderMock.Object, _logger);

            // Act
            await plugin.SuggestTextEditAsync(42, "<p>Suggested copy</p>");

            // Assert
            var message = Assert.Single(chatContextWithoutFeature.AssistantPostMessagesToBeProcessed);
            var update = JsonConvert.DeserializeObject<TextSuggestionUpdate>((string)message.Metadata[CommonMetadataConsts.Data]);
            Assert.Equal(string.Empty, update.FeatureName);
        }

        [Fact]
        public async Task SuggestTextEditAsync_ReturnsConfirmationMessage_WithoutRepeatingSuggestedText()
        {
            // Arrange
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);
            const string suggestedText = "<p>Suggested copy that should not be echoed back</p>";

            // Act
            var result = await plugin.SuggestTextEditAsync(42, suggestedText);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(result));
            Assert.DoesNotContain(suggestedText, result);
        }

        [Fact]
        public async Task SuggestTextEditAsync_NullSuggestedText_SendsEmptyStringAndSucceeds()
        {
            // Arrange
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);

            // Act
            var result = await plugin.SuggestTextEditAsync(7, null);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(result));
            var message = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            // Content must fall back to a non-empty placeholder — never regress into the empty
            // string that crashes Anthropic's API when this message is replayed on the next turn.
            Assert.False(string.IsNullOrWhiteSpace(message.Content));
            var update = JsonConvert.DeserializeObject<TextSuggestionUpdate>((string)message.Metadata[CommonMetadataConsts.Data]);
            Assert.Equal(string.Empty, update.SuggestedText);
        }

        [Fact]
        public async Task SuggestTextEditAsync_WhitespaceOnlySuggestedText_ContentFallsBackToNonEmptyPlaceholder()
        {
            // Arrange — SuggestedText defaults null-to-empty but not whitespace-to-empty, so this
            // guards the content fallback against the whitespace-only edge case too.
            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);

            // Act
            await plugin.SuggestTextEditAsync(42, "   ");

            // Assert
            var message = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            Assert.False(string.IsNullOrWhiteSpace(message.Content));
            Assert.Equal("Text suggestion for the selected feature.", message.Content);
        }

        [Fact]
        public async Task SuggestTextEditAsync_WhenBuildingMessageThrows_SendsFailedToolExecutionUpdateAndReturnsEmpty()
        {
            // Arrange
            _dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Throws(new InvalidOperationException("boom"));

            var tool = BuildTool();
            var plugin = new TextSuggestionPlugin(tool, _notificationServiceMock.Object, _chatContext, _dateTimeProviderMock.Object, _logger);

            // Act
            var result = await plugin.SuggestTextEditAsync(42, "<p>Suggested copy</p>");

            // Assert
            Assert.Equal(string.Empty, result);
            Assert.Empty(_chatContext.AssistantPostMessagesToBeProcessed);
            _notificationServiceMock.Verify(
                x => x.SendToolExecutionUpdate(
                    SessionId,
                    ToolName,
                    "failed",
                    It.IsAny<string>(),
                    500,
                    null),
                Times.Once);
        }

        private static TenantTool BuildTool() =>
            new()
            {
                Id = 24,
                Name = ToolName,
                Type = "Integration_Amais",
                Config = "{\"isVoiceEnabled\":false,\"areSkillsEnabled\":true}"
            };
    }
}
