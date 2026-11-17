using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Models;
using System;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>US 7959: the live failed-turn marker: shape, deterministic id, no exception text.</summary>
    public class FailedTurnMarkerTests
    {
        private static readonly DateTimeOffset FailedAt = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

        [Fact]
        public void BuildLiveMessage_TerminalError_HasTheContractShape()
        {
            var failure = ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable, 12, "Helper", "https://icons/helper.png", ErrorConst.AgentNotRunnable);

            var message = FailedTurnMarker.BuildLiveMessage("turn-1", failure, FailedAt);

            Assert.Equal("assistant", message.Type);
            Assert.Equal(ChannelMessageType.Metadata, message.ChannelMessageType);
            Assert.Equal(FailedTurnMarker.ErrorText, message.Content);
            Assert.Equal(FailedAt, message.Timestamp);
            Assert.True(Guid.TryParse(message.Id, out _));

            var metadata = message.Metadata;
            Assert.Equal(true, metadata[CommonMetadataConsts.RenderAsSystem]);
            Assert.Equal(true, metadata[CommonMetadataConsts.IsError]);
            Assert.Equal(ChatErrorCodes.ChatAgentUnavailable, metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(ErrorConst.AgentNotRunnable, metadata[CommonMetadataConsts.DetailCode]);
            Assert.Equal(ChatErrorSeverity.Error, metadata[CommonMetadataConsts.Severity]);
            Assert.Equal(true, metadata[CommonMetadataConsts.IsTerminal]);
            Assert.Equal("turn-1", metadata[CommonMetadataConsts.TurnId]);
            Assert.Equal(12, metadata[CommonMetadataConsts.AgentId]);
            Assert.Equal("Helper", metadata[CommonMetadataConsts.AgentName]);
            Assert.Equal("https://icons/helper.png", metadata[CommonMetadataConsts.ChatbotIconUrl]);
            Assert.Equal("2026-10-21T09:30:00.0000000+00:00", metadata[CommonMetadataConsts.FailedAt]);
            Assert.False(metadata.ContainsKey(CommonMetadataConsts.IsStoppedMarker));
        }

        [Fact]
        public void BuildLiveMessage_ToolWarning_IsANonTerminalWarning()
        {
            var message = FailedTurnMarker.BuildLiveMessage("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatToolUnavailable, 12), FailedAt);

            Assert.Equal(FailedTurnMarker.ToolWarningText, message.Content);
            Assert.Equal(ChatErrorSeverity.Warning, message.Metadata[CommonMetadataConsts.Severity]);
            Assert.Equal(false, message.Metadata[CommonMetadataConsts.IsTerminal]);
        }

        [Fact]
        public void BuildLiveMessage_OptionalKeysAreLeftOutWhenUnknown()
        {
            var metadata = FailedTurnMarker.BuildLiveMessage("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatTurnFailed), FailedAt).Metadata;

            Assert.False(metadata.ContainsKey(CommonMetadataConsts.DetailCode));
            Assert.False(metadata.ContainsKey(CommonMetadataConsts.AgentId));
            Assert.False(metadata.ContainsKey(CommonMetadataConsts.AgentName));
            Assert.False(metadata.ContainsKey(CommonMetadataConsts.ChatbotIconUrl));
        }

        [Fact]
        public void BuildLiveMessage_HandBuiltFailure_StillOnlyCarriesAllowlistedCodes()
        {
            const string sentinel = "SENTINEL-db-password-in-connection-string";
            var failure = new ChatTurnFailure { Code = sentinel, DetailCode = sentinel, Severity = sentinel };

            var message = FailedTurnMarker.BuildLiveMessage("turn-1", failure, FailedAt);

            Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(message));
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, message.Metadata[CommonMetadataConsts.ErrorCode]);
            Assert.Equal(ChatErrorSeverity.Error, message.Metadata[CommonMetadataConsts.Severity]);
        }

        [Fact]
        public void BuildMessageId_IsDeterministicPerTurnAndAgent()
        {
            var first = FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatModelTimeout, 12));
            var again = FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatModelUnavailable, 12));

            Assert.Equal(first, again);
            Assert.NotEqual(first, FailedTurnMarker.BuildMessageId("turn-2", ChatTurnFailure.Create(ChatErrorCodes.ChatModelTimeout, 12)));
            Assert.NotEqual(first, FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatModelTimeout, 13)));
        }

        [Fact]
        public void BuildMessageId_WarningsDifferFromTheTurnErrorAndFromEachOther()
        {
            var error = FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatModelTimeout, 12));
            var toolUnavailable = FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatToolUnavailable, 12));
            var toolFailed = FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatToolFailed, 12));

            Assert.NotEqual(error, toolUnavailable);
            Assert.NotEqual(toolUnavailable, toolFailed);
            Assert.Equal(toolFailed, FailedTurnMarker.BuildMessageId("turn-1", ChatTurnFailure.Create(ChatErrorCodes.ChatToolFailed, 12)));
        }
    }
}
