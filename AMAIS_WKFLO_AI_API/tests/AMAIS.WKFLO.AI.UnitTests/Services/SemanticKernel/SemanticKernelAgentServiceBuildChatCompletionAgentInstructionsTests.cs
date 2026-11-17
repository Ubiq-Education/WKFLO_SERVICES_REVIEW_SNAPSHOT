using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers Task #7032: the "Prompt Considerations" section of the LLM Instructions built by
    /// SemanticKernelAgentService.BuildChatCompletionAgentInstructions must be qualified by whether the
    /// agent is tagged with the "Maisy" feature-selection tag.
    ///
    /// - Non-Maisy agents (isMaisyAgent: false) must still answer genuine capability questions ("what can
    ///   you help with?") using the tool list, but must be instructed to deflect meta/introspection
    ///   questions about their underlying tools/plugins/architecture without enumerating tool names or
    ///   descriptions.
    /// - Maisy agents (isMaisyAgent: true) must preserve the exact pre-#7032 instructions, unchanged.
    ///
    /// This method is also the single shared implementation now used by both
    /// RunChatCompletionAgentAsync (text) and RunVoiceChatCompletionAgentAsync (voice), replacing the
    /// previously copy-pasted Instructions-building block in each.
    /// </summary>
    public class SemanticKernelAgentServiceBuildChatCompletionAgentInstructionsTests
    {
        private static readonly DateTimeOffset UtcNow = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void NonMaisyAgent_InstructsModelToDeflectIntrospectionQuestionsWithoutEnumeratingTools()
        {
            var result = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "You are a helpful assistant.", amaisContextLine: string.Empty, assistantReasoning: null, isMaisyAgent: false);

            // Must explicitly instruct the model not to reveal/enumerate tool or plugin names/descriptions
            // when asked a meta/introspection question about its underlying tooling.
            Assert.Contains("meta or introspection question", result);
            Assert.Contains("do NOT reveal, list, or describe any tool/plugin names, descriptions, or implementation details", result);
        }

        [Fact]
        public void NonMaisyAgent_StillAnswersGenuineCapabilityQuestionsUsingToolList()
        {
            var result = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "You are a helpful assistant.", amaisContextLine: string.Empty, assistantReasoning: null, isMaisyAgent: false);

            // The friendly "what can you help with" guidance must still be present and still tell the
            // model to answer from its actual tools/plugins (name + description).
            Assert.Contains("When the user asks what you can help with or what your capabilities are, answer using your actual tools/plugins listed above (name + description)", result);
        }

        [Fact]
        public void MaisyAgent_PreservesCurrentBehaviorWithNoDeflectionInstruction()
        {
            var result = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "You are a helpful assistant.", amaisContextLine: string.Empty, assistantReasoning: null, isMaisyAgent: true);

            // Maisy agents must not receive the deflection instruction — behavior is unchanged from
            // before Task #7032.
            Assert.DoesNotContain("meta or introspection question", result);
            Assert.DoesNotContain("do NOT reveal, list, or describe any tool/plugin names", result);

            // The original, unqualified capability-question guidance must still be present verbatim.
            Assert.Contains("- When describing tools or plugins, use the exact name and description provided by the plugin but avoid long IDs.", result);
            Assert.Contains("When the user asks what you can help with or what your capabilities are, answer using your actual tools/plugins listed above (name + description)", result);
        }

        [Fact]
        public void MaisyAgent_And_NonMaisyAgent_ShareIdenticalNonPromptConsiderationsSections()
        {
            var maisyResult = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "Prompt text", "Context line", "Some reasoning", isMaisyAgent: true);
            var nonMaisyResult = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "Prompt text", "Context line", "Some reasoning", isMaisyAgent: false);

            // The date/time header, prompt, context line, previous reasoning, and Responses Considerations
            // must be identical regardless of the Maisy flag — only Prompt Considerations differs.
            Assert.Contains($"Today: {UtcNow:yyyy-MM-dd} {UtcNow:HH:mm:ss} UTC", maisyResult);
            Assert.Contains($"Today: {UtcNow:yyyy-MM-dd} {UtcNow:HH:mm:ss} UTC", nonMaisyResult);
            Assert.Contains("Prompt: Prompt text", maisyResult);
            Assert.Contains("Prompt: Prompt text", nonMaisyResult);
            Assert.Contains("Context line", maisyResult);
            Assert.Contains("Context line", nonMaisyResult);
            Assert.Contains("Previous Reasoning Done: Some reasoning", maisyResult);
            Assert.Contains("Previous Reasoning Done: Some reasoning", nonMaisyResult);
            Assert.Contains("You are an HTML-only response generator.", maisyResult);
            Assert.Contains("You are an HTML-only response generator.", nonMaisyResult);
        }

        [Fact]
        public void NullAssistantReasoning_FallsBackToNoneText()
        {
            var result = SemanticKernelAgentService.BuildChatCompletionAgentInstructions(
                UtcNow, "Prompt text", amaisContextLine: string.Empty, assistantReasoning: null, isMaisyAgent: true);

            Assert.Contains("Previous Reasoning Done: None.", result);
        }
    }
}
