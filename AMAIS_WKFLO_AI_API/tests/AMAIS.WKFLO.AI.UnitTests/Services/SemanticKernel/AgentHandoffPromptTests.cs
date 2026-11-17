using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using AMAIS.WKFLO.Common.AI.Application.Plugins.AgentEventPublisher;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    // Bug 8007: an agent that decides to delegate must call publish_agent_event, not just announce it.
    public class AgentHandoffPromptTests
    {
        private static readonly List<SemanticKernelAgentService.AllowedSpinUpAgentTarget> Targets =
        [
            new("Billing", "Handles refunds and invoices"),
            new("Support", null)
        ];

        [Fact]
        public void BuildAgentHandoffPrompt_RequiresToolCallInSameResponse()
        {
            var prompt = SemanticKernelAgentService.BuildAgentHandoffPrompt(Targets, "I need a refund");

            Assert.Contains("you MUST call publish_agent_event in that same response", prompt);
            Assert.Contains("does not need to type @", prompt);
            Assert.Contains("Never say that you will ask, forward, call or notify an agent without actually making the call", prompt);
        }

        [Fact]
        public void BuildAgentHandoffPrompt_NoLongerOffersSuggestWithAtAlternative()
        {
            var prompt = SemanticKernelAgentService.BuildAgentHandoffPrompt(Targets, "I need a refund");

            Assert.DoesNotContain("suggest it to the user", prompt);
            Assert.DoesNotContain("prefixed with @", prompt);
        }

        [Fact]
        public void BuildAgentHandoffPrompt_ListsAgentsByPlainName()
        {
            var prompt = SemanticKernelAgentService.BuildAgentHandoffPrompt(Targets, "I need a refund");

            Assert.Contains("- Billing: Handles refunds and invoices", prompt);
            Assert.Contains("- Support: (no description provided)", prompt);
            Assert.DoesNotContain("- @Billing", prompt);
        }

        [Fact]
        public void BuildAgentHandoffPrompt_NoMention_OmitsMandatoryBlock()
        {
            var prompt = SemanticKernelAgentService.BuildAgentHandoffPrompt(Targets, "I need a refund");

            Assert.DoesNotContain("MANDATORY TOOL CALL", prompt);
        }

        [Fact]
        public void BuildAgentHandoffPrompt_AtMention_KeepsMandatoryBlock()
        {
            var prompt = SemanticKernelAgentService.BuildAgentHandoffPrompt(Targets, "Please ask @Billing about this");

            Assert.Contains("AGENT HAND-OFF — MANDATORY TOOL CALL:", prompt);
            Assert.Contains("mentions the following agent(s) by name using @: Billing.", prompt);
        }

        [Fact]
        public void PublishAgentEventDescription_DoesNotTieToolToAtMention()
        {
            var method = typeof(AgentEventPublisherPlugin).GetMethod(nameof(AgentEventPublisherPlugin.PublishAgentEventAsync));
            var description = method.GetCustomAttribute<DescriptionAttribute>().Description;

            Assert.DoesNotContain("pattern to recognize", description);
            Assert.Contains("whether or not the user typed @", description);
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_NamedTargetAndNoCall_IsMiss()
        {
            var result = SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "I'll ask billing to look into this.", ["Billing", "Support"], publishCalled: false);

            Assert.Equal(["Billing"], result);
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_ToolWasCalled_IsNotMiss()
        {
            var result = SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "I'll ask Billing to look into this.", ["Billing"], publishCalled: true);

            Assert.Empty(result);
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_NoTargetNamed_IsNotMiss()
        {
            var result = SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "Here is your answer.", ["Billing"], publishCalled: false);

            Assert.Empty(result);
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_NoTargetsOrEmptyReply_IsNotMiss()
        {
            Assert.Empty(SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs("Billing", null, false));
            Assert.Empty(SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs("", ["Billing"], false));
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_SubstringOfLongerWord_IsNotMiss()
        {
            Assert.Empty(SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "That is already supported.", ["Support"], publishCalled: false));
            Assert.Equal(["Support"], SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "I'll ask support, then come back.", ["Support"], publishCalled: false));
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_NameWithRegexChars_IsEscaped()
        {
            var result = SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "I'll ask Billing (EU) now.", ["Billing (EU)", "Bill.ng"], publishCalled: false);

            Assert.Equal(["Billing (EU)"], result);
        }

        [Fact]
        public void FindAnnouncedButUncalledHandoffs_ApprovalRequested_IsNotMiss()
        {
            var result = SemanticKernelAgentService.FindAnnouncedButUncalledHandoffs(
                "I'll ask Billing.", ["Billing"], publishCalled: false, approvalRequested: true);

            Assert.Empty(result);
        }

        private static (AgentEventPublisherPlugin Plugin, ChatContext Context) CreatePlugin()
        {
            var context = new ChatContext();
            var plugin = new AgentEventPublisherPlugin(new Mock<IWkfloMediator>().Object, new Mock<IChatNotificationService>().Object,
                context, new TenantContext(), new Mock<IMasterDataDbContext>().Object, NullLogger.Instance, 1,
                new Mock<IBlobStorageProvider>().Object);
            return (plugin, context);
        }

        [Fact]
        public async Task PublishAgentEventAsync_InvalidTargetJson_StillSetsCalledFlag()
        {
            var (plugin, context) = CreatePlugin();

            var result = await plugin.PublishAgentEventAsync("not json", "hi", "test", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("not valid JSON", result);
            Assert.True(context.AgentEventPublishCalledThisTurn);
        }

        [Fact]
        public async Task PublishAgentEventAsync_EmptyTargets_SetsCalledFlag()
        {
            var (plugin, context) = CreatePlugin();

            await plugin.PublishAgentEventAsync("[]", "hi", "test", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(context.AgentEventPublishCalledThisTurn);
        }

        [Fact]
        public void ChatContextMapping_DoesNotRestoreCalledFlag()
        {
            var source = new ChatContext { SessionId = "s1", AgentEventPublishCalledThisTurn = true };
            var destination = new ChatContext();

            WkfloMapperExtensions.CreateMapper().Map(source, destination);

            Assert.Equal("s1", destination.SessionId);
            Assert.False(destination.AgentEventPublishCalledThisTurn);
        }
    }
}
