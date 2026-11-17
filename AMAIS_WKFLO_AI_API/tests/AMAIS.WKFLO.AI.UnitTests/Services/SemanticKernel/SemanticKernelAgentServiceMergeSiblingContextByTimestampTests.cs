using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers US 7078: MergeSiblingContextByTimestamp must interleave a child agent's injected context
    /// with the root's own history by actual timestamp, not append it as a trailing block — otherwise
    /// content the child produced long ago would land after everything the root has already said.
    /// </summary>
    public class SemanticKernelAgentServiceMergeSiblingContextByTimestampTests
    {
        private static ChatMessageContent BuildMessage(string content, DateTimeOffset timestamp, AuthorRole? role = null) =>
            new(role ?? AuthorRole.Assistant, content, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, timestamp }
            });

        [Fact]
        public void MergeSiblingContextByTimestamp_NoSiblingContent_ReturnsSameInstanceUnchanged()
        {
            var chatHistory = new ChatHistory { BuildMessage("hello", DateTimeOffset.UtcNow) };

            var result = SemanticKernelAgentService.MergeSiblingContextByTimestamp(chatHistory, []);

            Assert.Same(chatHistory, result);
        }

        [Fact]
        public void MergeSiblingContextByTimestamp_SiblingContentOlderThanRootsLatestTurn_IsInterleavedNotAppended()
        {
            var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var t2 = new DateTimeOffset(2026, 1, 1, 10, 5, 0, TimeSpan.Zero);
            var t3 = new DateTimeOffset(2026, 1, 1, 10, 10, 0, TimeSpan.Zero);

            // Root's own history already spans t1..t3; the sibling's injected content (t2) happened
            // in between two of the root's own turns, not after all of them.
            var chatHistory = new ChatHistory
            {
                BuildMessage("root turn 1", t1),
                BuildMessage("root turn 3", t3)
            };
            var siblingContext = new List<ChatMessageContent> { BuildMessage("sibling turn 2", t2) };

            var result = SemanticKernelAgentService.MergeSiblingContextByTimestamp(chatHistory, siblingContext);

            Assert.Equal(["root turn 1", "sibling turn 2", "root turn 3"], result.Select(m => m.Content));
        }

        [Fact]
        public void MergeSiblingContextByTimestamp_MultipleSiblingMessagesSpanningWholeSession_AllInterleavedCorrectly()
        {
            var times = Enumerable.Range(0, 5).Select(i => new DateTimeOffset(2026, 1, 1, 10, i, 0, TimeSpan.Zero)).ToArray();

            var chatHistory = new ChatHistory
            {
                BuildMessage("root@0", times[0]),
                BuildMessage("root@4", times[4])
            };
            var siblingContext = new List<ChatMessageContent>
            {
                BuildMessage("sibling@1", times[1]),
                BuildMessage("sibling@2", times[2]),
                BuildMessage("sibling@3", times[3])
            };

            var result = SemanticKernelAgentService.MergeSiblingContextByTimestamp(chatHistory, siblingContext);

            Assert.Equal(["root@0", "sibling@1", "sibling@2", "sibling@3", "root@4"], result.Select(m => m.Content));
        }

        [Fact]
        public void MergeSiblingContextByTimestamp_PinnedSystemMessage_StaysFirstEvenWithEarlierSiblingTimestamp()
        {
            // The root's pinned AuthorRole.System message (chatHistory[0] convention) MUST remain
            // first no matter what — Anthropic rejects a 'system' message anywhere else in the array.
            // A plain timestamp sort would fail this: the sibling message below carries an earlier
            // timestamp than the pinned system message, so ordering by timestamp alone would push the
            // system message to position 1.
            var systemTimestamp = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var earlierSiblingTimestamp = systemTimestamp.AddMinutes(-30);

            var chatHistory = new ChatHistory
            {
                BuildMessage("pinned system context", systemTimestamp, AuthorRole.System),
                BuildMessage("root turn", systemTimestamp.AddMinutes(5))
            };
            var siblingContext = new List<ChatMessageContent> { BuildMessage("sibling turn", earlierSiblingTimestamp) };

            var result = SemanticKernelAgentService.MergeSiblingContextByTimestamp(chatHistory, siblingContext);

            Assert.Equal("pinned system context", result.First().Content);
            Assert.Equal(AuthorRole.System, result.First().Role);
            Assert.Equal(["pinned system context", "sibling turn", "root turn"], result.Select(m => m.Content));
        }

        [Fact]
        public void MergeSiblingContextByTimestamp_NeverMutatesTheOriginalChatHistoryInstance()
        {
            var chatHistory = new ChatHistory { BuildMessage("root turn", DateTimeOffset.UtcNow) };
            var originalCount = chatHistory.Count;
            var siblingContext = new List<ChatMessageContent> { BuildMessage("sibling turn", DateTimeOffset.UtcNow.AddMinutes(1)) };

            var result = SemanticKernelAgentService.MergeSiblingContextByTimestamp(chatHistory, siblingContext);

            Assert.NotSame(chatHistory, result);
            Assert.Equal(originalCount, chatHistory.Count);
        }
    }
}
