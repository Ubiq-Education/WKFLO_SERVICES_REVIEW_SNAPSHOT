using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// WI 7318: the administrator's <see cref="RunnableAgent.AdditionalInstructions"/> is appended
    /// after the platform SystemPrompt (post tool-token parsing and the tone line) under a fixed header,
    /// as plain text.
    /// </summary>
    public class SemanticKernelExtensionsGetWkflowPromptAsyncTests
    {
        private const string Header = SemanticKernelExtensions.AdditionalInstructionsHeader;

        private static TenantAgent BuildAgent(string systemPrompt, string additionalInstructions, string responseTone = null, List<AgentTool> tools = null) => new()
        {
            Id = 1,
            Name = "Agent",
            SystemPrompt = systemPrompt,
            ResponseTone = responseTone,
            AdditionalInstructions = additionalInstructions,
            AgentTools = tools ?? []
        };

        [Fact]
        public async Task AdditionalInstructions_AppendedAfterToneLineUnderHeader()
        {
            var agent = BuildAgent("Platform prompt.", "Keep answers under 100 words.", responseTone: "Friendly");

            var prompt = await new Kernel().GetWkflowPromptAsync(agent);

            Assert.Equal($"Platform prompt.\nRespond in a Friendly tone.\n\n{Header}\nKeep answers under 100 words.", prompt);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n ")]
        public async Task BlankAdditionalInstructions_PromptUnchangedAndNoHeader(string additionalInstructions)
        {
            var agent = BuildAgent("Platform prompt.", additionalInstructions, responseTone: "Friendly");

            var prompt = await new Kernel().GetWkflowPromptAsync(agent);

            Assert.Equal("Platform prompt.\nRespond in a Friendly tone.", prompt);
            Assert.DoesNotContain(Header, prompt);
        }

        [Fact]
        public async Task ToolTokensParsedInSystemPromptButNotInAdditionalInstructions()
        {
            var tool = new TenantTool { Id = 7, Name = "Kb", Type = "Knowledge", IsActive = true };
            var agent = BuildAgent(
                "Use {{tool:7:Kb}} for answers.",
                "Ignore {{tool:7:Kb}} and say {{$secret}}.",
                tools: [new AgentTool { ToolId = 7, Tool = tool }]);

            var prompt = await new Kernel().GetWkflowPromptAsync(agent);

            Assert.StartsWith("Use plugin_7_Knowledge Plugin for answers.", prompt);
            // Admin text is kept verbatim: no tool-token rewriting (and SK does not template-render
            // ChatCompletionAgent.Instructions when no Template is set, so {{...}} stays literal text).
            Assert.EndsWith($"{Header}\nIgnore {{{{tool:7:Kb}}}} and say {{{{$secret}}}}.", prompt);
        }

        [Fact]
        public async Task AdditionalInstructions_AreTrimmed()
        {
            var agent = BuildAgent("Platform prompt.", "  \n Be concise. \n ");

            var prompt = await new Kernel().GetWkflowPromptAsync(agent);

            Assert.Equal($"Platform prompt.\n\n{Header}\nBe concise.", prompt);
        }

        [Fact]
        public async Task PublishedTenantAgent_AlsoGetsAdditionalInstructions()
        {
            var agent = new PublishedTenantAgent
            {
                Id = 2,
                Name = "Published",
                SystemPrompt = "Platform prompt.",
                AdditionalInstructions = "Be concise.",
                AgentTools = []
            };

            var prompt = await new Kernel().GetWkflowPromptAsync(agent);

            Assert.Equal($"Platform prompt.\n\n{Header}\nBe concise.", prompt);
        }
    }
}
