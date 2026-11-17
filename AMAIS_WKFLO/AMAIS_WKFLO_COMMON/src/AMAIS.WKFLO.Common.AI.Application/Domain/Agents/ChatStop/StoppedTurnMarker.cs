using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Globalization;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7502: the "Stopped. All agents cancelled." divider a turn stopped by the user saves at the end of its own
    /// history (<see cref="CommonMetadataConsts.IsStoppedMarker"/>). It is only shown to people: it is detached from
    /// the history sent to the model and put back before the history is saved again.
    /// </summary>
    public static class StoppedTurnMarker
    {
        public const string Text = "Stopped. All agents cancelled.";

        /// <summary>The marker as saved in the chat history: Assistant role, rendered as a system divider.</summary>
        public static ChatMessageContent Build(string messageId, DateTimeOffset timestamp, DateTimeOffset stoppedAt) =>
            new(AuthorRole.Assistant, Text, metadata: new Dictionary<string, object>
            {
                [CommonMetadataConsts.Timestamp] = timestamp,
                [CommonMetadataConsts.MessageId] = messageId,
                [CommonMetadataConsts.ChannelMessageType] = ChannelMessageType.Metadata,
                [CommonMetadataConsts.RenderAsSystem] = true,
                [CommonMetadataConsts.IsStoppedMarker] = true,
                [CommonMetadataConsts.StoppedAt] = FormatStoppedAt(stoppedAt)
            });

        /// <summary>The same marker as sent live to the chat (SignalR <c>ReceiveMessage</c>).</summary>
        public static ChatMessage BuildLiveMessage(string messageId, DateTimeOffset timestamp, DateTimeOffset stoppedAt) => new()
        {
            Id = messageId,
            Type = "assistant",
            Content = Text,
            ChannelMessageType = ChannelMessageType.Metadata,
            Timestamp = timestamp,
            Metadata = BuildReadModelMetadata(stoppedAt)
        };

        /// <summary>The marker keys exposed to the chat UI (live message and history read model), as plain values.</summary>
        public static Dictionary<string, object> BuildReadModelMetadata(DateTimeOffset stoppedAt) => new()
        {
            [CommonMetadataConsts.RenderAsSystem] = true,
            [CommonMetadataConsts.IsStoppedMarker] = true,
            [CommonMetadataConsts.StoppedAt] = FormatStoppedAt(stoppedAt)
        };

        public static string FormatStoppedAt(DateTimeOffset stoppedAt) =>
            stoppedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        public static bool IsMarker(ChatMessageContent message) => IsMarker(message?.Metadata);

        public static bool IsMarker(IReadOnlyDictionary<string, object> metadata) =>
            ChatHistoryMessageExtractor.IsFlagSet(metadata, CommonMetadataConsts.IsStoppedMarker);

        /// <summary>Reads <see cref="CommonMetadataConsts.StoppedAt"/>, whether it is fresh or came back from the blob as JSON.</summary>
        public static DateTimeOffset? ReadStoppedAt(IReadOnlyDictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.StoppedAt, out var value) || value == null)
                return null;

            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt),
                JsonElement element when element.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
                string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed,
                _ => null
            };
        }

        /// <summary>
        /// Removes the markers from <paramref name="chatHistory"/> (in place) so they are never sent to the model, and
        /// returns them so <see cref="Reattach"/> can put them back before the history is saved.
        /// </summary>
        public static IReadOnlyList<ChatMessageContent> Detach(ChatHistory chatHistory)
        {
            if (chatHistory == null || chatHistory.Count == 0)
                return [];

            var markers = new List<ChatMessageContent>();
            for (var i = chatHistory.Count - 1; i >= 0; i--)
            {
                if (IsMarker(chatHistory[i]))
                {
                    markers.Insert(0, chatHistory[i]);
                    chatHistory.RemoveAt(i);
                }
            }

            return markers;
        }

        /// <summary>
        /// Puts detached markers back (in place): each one right before the first message timestamped after it, or at
        /// the end. Messages without a timestamp (tool calls and results) are never split from the turn before them.
        /// </summary>
        public static ChatHistory Reattach(ChatHistory chatHistory, IReadOnlyList<ChatMessageContent> markers)
        {
            if (chatHistory == null || markers == null || markers.Count == 0)
                return chatHistory;

            foreach (var marker in markers)
            {
                var markerTime = ChatHistoryMessageExtractor.GetTimestampFromMetadata(marker.Metadata);
                var insertAt = chatHistory.Count;
                if (markerTime.HasValue)
                {
                    for (var i = 0; i < chatHistory.Count; i++)
                    {
                        var time = ChatHistoryMessageExtractor.GetTimestampFromMetadata(chatHistory[i].Metadata);
                        if (time.HasValue && time.Value > markerTime.Value)
                        {
                            insertAt = i;
                            break;
                        }
                    }
                }

                chatHistory.Insert(insertAt, marker);
            }

            return chatHistory;
        }

        /// <summary>
        /// Merged multi-agent history: every agent stopped by the same Stop saved its own marker. Keeps one divider per
        /// Stop (same <see cref="CommonMetadataConsts.StoppedAt"/>): the last one, so it sits after every agent's
        /// messages before that Stop. <paramref name="orderedMessages"/> must already be in display order.
        /// </summary>
        public static void RemoveDuplicateMarkers<T>(List<T> orderedMessages, Func<T, IReadOnlyDictionary<string, object>> metadataOf)
        {
            if (orderedMessages == null || orderedMessages.Count < 2)
                return;

            var seen = new HashSet<long>();
            for (var i = orderedMessages.Count - 1; i >= 0; i--)
            {
                var metadata = metadataOf(orderedMessages[i]);
                if (!IsMarker(metadata) || ReadStoppedAt(metadata) is not { } stoppedAt)
                    continue;

                if (!seen.Add(stoppedAt.UtcTicks))
                    orderedMessages.RemoveAt(i);
            }
        }
    }
}
