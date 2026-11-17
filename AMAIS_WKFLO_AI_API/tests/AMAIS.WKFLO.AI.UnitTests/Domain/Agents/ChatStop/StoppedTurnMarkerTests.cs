using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatStop
{
    /// <summary>US 7502: the "Stopped. All agents cancelled." divider saved by a stopped turn.</summary>
    public class StoppedTurnMarkerTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        private static ChatMessageContent Message(AuthorRole role, string text, DateTimeOffset? timestamp)
        {
            var metadata = new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() };
            if (timestamp.HasValue)
                metadata[CommonMetadataConsts.Timestamp] = timestamp.Value;

            return new ChatMessageContent(role, text, metadata: metadata);
        }

        // The blob store serialises the history with System.Text.Json: metadata comes back as JsonElement.
        private static ChatHistory RoundTrip(ChatHistory history) =>
            JsonSerializer.Deserialize<ChatHistory>(JsonSerializer.Serialize(history));

        [Fact]
        public void Build_ReturnsAssistantDividerWithMarkerMetadata()
        {
            var marker = StoppedTurnMarker.Build("marker-1", T0, T0.AddSeconds(-2));

            Assert.Equal(AuthorRole.Assistant, marker.Role);
            Assert.Equal("Stopped. All agents cancelled.", marker.Content);
            Assert.Equal("marker-1", marker.Metadata[CommonMetadataConsts.MessageId]);
            Assert.Equal(T0, marker.Metadata[CommonMetadataConsts.Timestamp]);
            Assert.Equal(ChannelMessageType.Metadata, marker.Metadata[CommonMetadataConsts.ChannelMessageType]);
            Assert.Equal(true, marker.Metadata[CommonMetadataConsts.RenderAsSystem]);
            Assert.Equal(true, marker.Metadata[CommonMetadataConsts.IsStoppedMarker]);
            Assert.Equal("2026-10-19T09:59:58.0000000+00:00", marker.Metadata[CommonMetadataConsts.StoppedAt]);
        }

        [Fact]
        public void BuildLiveMessage_MatchesThePersistedMarker()
        {
            var live = StoppedTurnMarker.BuildLiveMessage("marker-1", T0, T0.AddSeconds(-2));

            Assert.Equal("marker-1", live.Id);
            Assert.Equal("assistant", live.Type);
            Assert.Equal(StoppedTurnMarker.Text, live.Content);
            Assert.Equal(ChannelMessageType.Metadata, live.ChannelMessageType);
            Assert.Equal(T0, live.Timestamp);
            Assert.Equal(true, live.Metadata[CommonMetadataConsts.RenderAsSystem]);
            Assert.Equal(true, live.Metadata[CommonMetadataConsts.IsStoppedMarker]);
            Assert.Equal("2026-10-19T09:59:58.0000000+00:00", live.Metadata[CommonMetadataConsts.StoppedAt]);
        }

        [Fact]
        public void IsMarkerAndReadStoppedAt_WorkAfterABlobRoundTrip()
        {
            var history = RoundTrip(new ChatHistory { StoppedTurnMarker.Build("marker-1", T0, T0), Message(AuthorRole.Assistant, "Normal", T0) });

            Assert.True(StoppedTurnMarker.IsMarker(history[0]));
            Assert.False(StoppedTurnMarker.IsMarker(history[1]));
            Assert.Equal(T0, StoppedTurnMarker.ReadStoppedAt(history[0].Metadata));
        }

        [Fact]
        public void Detach_RemovesMarkersFromTheModelHistoryAndReturnsThem()
        {
            var question = Message(AuthorRole.User, "Question", T0);
            var partial = Message(AuthorRole.Assistant, "Partial", T0.AddSeconds(1));
            var marker = StoppedTurnMarker.Build("marker-1", T0.AddSeconds(2), T0.AddSeconds(1));
            var next = Message(AuthorRole.User, "Next question", T0.AddSeconds(10));
            var history = RoundTrip(new ChatHistory { question, partial, marker, next });

            var detached = StoppedTurnMarker.Detach(history);

            Assert.Single(detached);
            Assert.True(StoppedTurnMarker.IsMarker(detached[0]));
            Assert.Equal(new[] { "Question", "Partial", "Next question" }, history.Select(m => m.Content));
        }

        [Fact]
        public void Reattach_PutsEachMarkerBeforeTheFirstLaterMessage_AfterUntimestampedToolMessages()
        {
            var marker = StoppedTurnMarker.Build("marker-1", T0.AddSeconds(5), T0.AddSeconds(4));
            var history = new ChatHistory
            {
                Message(AuthorRole.User, "Question", T0),
                new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent("search", "Docs", "call-1")]),
                new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("search", "Docs", "call-1", "result")]),
                Message(AuthorRole.User, "Next question", T0.AddSeconds(10)),
                Message(AuthorRole.Assistant, "Next answer", T0.AddSeconds(11))
            };

            StoppedTurnMarker.Reattach(history, [marker]);

            Assert.Same(marker, history[3]);
            Assert.Equal("Next question", history[4].Content);
        }

        [Fact]
        public void Reattach_WithNoLaterMessage_AppendsTheMarker()
        {
            var marker = StoppedTurnMarker.Build("marker-1", T0.AddSeconds(5), T0.AddSeconds(4));
            var history = new ChatHistory { Message(AuthorRole.User, "Question", T0) };

            StoppedTurnMarker.Reattach(history, [marker]);

            Assert.Same(marker, history[^1]);
        }

        [Fact]
        public void DetachThenReattach_GivesBackTheSavedOrder()
        {
            var history = RoundTrip(new ChatHistory
            {
                Message(AuthorRole.User, "Question", T0),
                Message(AuthorRole.Assistant, "Partial", T0.AddSeconds(1)),
                StoppedTurnMarker.Build("marker-1", T0.AddSeconds(1).AddMilliseconds(1), T0.AddSeconds(1)),
                Message(AuthorRole.User, "Next question", T0.AddSeconds(10)),
                Message(AuthorRole.Assistant, "Next answer", T0.AddSeconds(11))
            });
            var expected = history.Select(m => m.Content).ToList();

            var detached = StoppedTurnMarker.Detach(history);
            StoppedTurnMarker.Reattach(history, detached);

            Assert.Equal(expected, history.Select(m => m.Content));
        }

        [Fact]
        public void RemoveDuplicateMarkers_KeepsTheLastMarkerOfEachStop()
        {
            var stopOne = T0;
            var stopTwo = T0.AddMinutes(5);
            var messages = new List<ChatMessageContent>
            {
                Message(AuthorRole.Assistant, "MAISY partial", T0.AddSeconds(1)),
                StoppedTurnMarker.Build("maisy-marker", T0.AddSeconds(2), stopOne),
                Message(AuthorRole.Assistant, "Kit partial", T0.AddSeconds(3)),
                StoppedTurnMarker.Build("kit-marker", T0.AddSeconds(4), stopOne),
                Message(AuthorRole.Assistant, "Later answer", T0.AddMinutes(4)),
                StoppedTurnMarker.Build("second-stop", T0.AddMinutes(5).AddSeconds(1), stopTwo)
            };

            StoppedTurnMarker.RemoveDuplicateMarkers(messages, m => m.Metadata);

            Assert.Equal(new[] { "MAISY partial", "Kit partial", StoppedTurnMarker.Text, "Later answer", StoppedTurnMarker.Text },
                messages.Select(m => m.Content));
            Assert.Equal("kit-marker", messages[2].Metadata[CommonMetadataConsts.MessageId]);
            Assert.Equal("second-stop", messages[4].Metadata[CommonMetadataConsts.MessageId]);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_SkipsTheMarker()
        {
            var questionId = Guid.NewGuid();
            var history = RoundTrip(new ChatHistory
            {
                new ChatMessageContent(AuthorRole.User, "Question", metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }),
                StoppedTurnMarker.Build(Guid.NewGuid().ToString(), T0, T0)
            });

            var (question, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, questionId);

            Assert.Equal("Question", question);
            Assert.Null(answer);
            Assert.Null(answerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_KeepsTheAnswerBeforeTheMarker()
        {
            var questionId = Guid.NewGuid();
            var partialId = Guid.NewGuid();
            var history = RoundTrip(new ChatHistory
            {
                new ChatMessageContent(AuthorRole.User, "Question", metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }),
                new ChatMessageContent(AuthorRole.Assistant, "Partial", metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = partialId.ToString() }),
                StoppedTurnMarker.Build(Guid.NewGuid().ToString(), T0, T0)
            });

            var (_, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, questionId);

            Assert.Equal("Partial", answer);
            Assert.Equal(partialId, answerMessageId);
        }
    }
}
