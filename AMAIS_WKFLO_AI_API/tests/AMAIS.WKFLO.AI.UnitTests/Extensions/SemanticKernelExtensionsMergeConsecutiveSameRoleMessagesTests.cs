using AMAIS.WKFLO.Common.AI.Application.Extensions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// Covers MergeConsecutiveSameRoleMessages, used to build a defensive, explicit merge of
    /// consecutive same-role turns for a single outgoing LLM request - not all Anthropic-hosting
    /// platforms reliably auto-merge these, and this codebase can produce runs of several consecutive
    /// Assistant messages (BaseAmaisPlugin, reasoning/image-analysis, the real reply).
    /// </summary>
    public class SemanticKernelExtensionsMergeConsecutiveSameRoleMessagesTests
    {
        [Fact]
        public void ConsecutiveAssistantMessages_AreMergedIntoOne()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "Hi");
            history.AddMessage(AuthorRole.Assistant, "Thinking about it...");
            history.AddMessage(AuthorRole.Assistant, "Here's my answer.");
            history.AddMessage(AuthorRole.User, "Thanks");

            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            Assert.Equal(3, result.Count);
            Assert.Equal(AuthorRole.User, result[0].Role);
            Assert.Equal(AuthorRole.Assistant, result[1].Role);
            Assert.Equal(AuthorRole.User, result[2].Role);
        }

        [Fact]
        public void ConsecutiveUserMessages_AreMergedIntoOne()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "Here's my question");
            history.AddMessage(AuthorRole.User, "and here's an attachment");
            history.AddMessage(AuthorRole.Assistant, "Got it");

            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            Assert.Equal(2, result.Count);
            Assert.Equal(AuthorRole.User, result[0].Role);
            Assert.Equal(AuthorRole.Assistant, result[1].Role);
        }

        [Fact]
        public void MergedMessage_PreservesItemsFromEveryMessageInTheRun()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "Hi");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, new ChatMessageContentItemCollection
            {
                new TextContent("Here's what I generated:")
            }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, new ChatMessageContentItemCollection
            {
                new ImageContent { Uri = new Uri("https://example.com/image.png") }
            }));

            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            var merged = Assert.Single(result, m => m.Role == AuthorRole.Assistant);
            Assert.Contains(merged.Items, i => i is TextContent);
            Assert.Contains(merged.Items, i => i is ImageContent);
        }

        [Fact]
        public void MergedMessage_KeepsFirstMessagesMetadata_FillingInMissingKeysFromLater()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.User, "Hi");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "First", metadata: new Dictionary<string, object>
            {
                ["messageId"] = "first-id",
                ["renderAsSystem"] = true
            }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "Second", metadata: new Dictionary<string, object>
            {
                ["messageId"] = "second-id",
                ["url"] = "https://example.com"
            }));

            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            var merged = Assert.Single(result, m => m.Role == AuthorRole.Assistant);
            Assert.Equal("first-id", merged.Metadata["messageId"]);
            Assert.Equal(true, merged.Metadata["renderAsSystem"]);
            Assert.Equal("https://example.com", merged.Metadata["url"]);
        }

        [Fact]
        public void SystemAndToolMessages_AreNeverMerged()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.System, "pinned background");
            history.AddMessage(AuthorRole.User, "Hi");
            history.AddMessage(AuthorRole.System, "should stay separate");
            history.AddMessage(AuthorRole.System, "also separate");

            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            Assert.Equal(4, result.Count);
        }

        [Fact]
        public void OriginalHistory_IsNeverMutated()
        {
            var history = new ChatHistory();
            history.AddMessage(AuthorRole.Assistant, "First");
            history.AddMessage(AuthorRole.Assistant, "Second");

            SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(history);

            Assert.Equal(2, history.Count);
        }

        [Fact]
        public void EmptyHistory_ReturnsEmptyHistory()
        {
            var result = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(new ChatHistory());

            Assert.Empty(result);
        }
    }
}
