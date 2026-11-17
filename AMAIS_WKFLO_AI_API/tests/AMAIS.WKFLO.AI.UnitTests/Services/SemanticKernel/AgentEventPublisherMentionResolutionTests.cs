using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    public class AgentEventPublisherMentionResolutionTests
    {
        [Fact]
        public void ExtractMentionedAgentNames_SingleMention_ReturnsMatchedName()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Hey @Billing, can you help with this refund?", ["Billing", "Support"]);

            Assert.Equal(["Billing"], result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_MultipleMentions_ReturnsAllMatches()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Loop in @Billing and @Support please.", ["Billing", "Support", "Legal"]);

            Assert.Equal(["Billing", "Support"], result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_CaseInsensitive_StillMatches()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Hey @billing can you take this?", ["Billing"]);

            Assert.Equal(["Billing"], result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_NoMention_ReturnsEmpty()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "No mentions in this message at all.", ["Billing", "Support"]);

            Assert.Empty(result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_IncidentalAtSign_DoesNotMatchUnrelatedName()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Please email me at john@example.com about this.", ["Billing", "Support"]);

            Assert.Empty(result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_AllowedNameCoincidentallyInsideEmailDomain_StillMatches()
        {
            // Known limitation: this is a plain "@" + name substring match (mirroring the plugin's own
            // exact-name resolution), not a word-boundary-aware tokenizer, so an allowed agent name that
            // happens to appear right after '@' in unrelated text (e.g. inside an email domain) is
            // indistinguishable from a genuine mention.
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Contact john@example.com for details.", ["example"]);

            Assert.Equal(["example"], result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_MultiWordAgentName_MatchesExactSubstring()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Please notify @Order Support about the delay.", ["Order Support"]);

            Assert.Equal(["Order Support"], result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_EmptyMessage_ReturnsEmpty()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(string.Empty, ["Billing"]);

            Assert.Empty(result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_NullAllowedNames_ReturnsEmpty()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames("Hey @Billing", null);

            Assert.Empty(result);
        }

        [Fact]
        public void ExtractMentionedAgentNames_DuplicateNamesInAllowedList_ReturnsDistinct()
        {
            var result = SemanticKernelAgentService.ExtractMentionedAgentNames(
                "Hey @Billing please help.", ["Billing", "Billing"]);

            Assert.Equal(["Billing"], result);
        }
    }
}
