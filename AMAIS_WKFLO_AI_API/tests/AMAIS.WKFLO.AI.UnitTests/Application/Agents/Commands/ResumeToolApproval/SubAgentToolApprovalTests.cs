using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Data;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// Bug 7722: "Ask for permission" in sub-agent runs. A hand-off from an interactive chat runs the target agent with
    /// approvals available (its Ask tools ask in the same chat), the root chat decides, and the resume re-checks the
    /// root's share and spin-up allowlist before running the stored call as the sub-agent.
    /// </summary>
    public class SubAgentToolApprovalTests
    {
        private const int RootAgentId = 10;
        private const int SubAgentId = 20;

        // ---- flag propagation -------------------------------------------------------------------------

        [Theory]
        [InlineData(RootAgentId)]
        [InlineData(null)]
        public async Task DomainEventHandler_ForwardsTheApprovalRootOntoTheCommand(int? root)
        {
            var mediator = new Mock<IWkfloMediator>();
            var handler = new PublishAgentEventDomainEventHandler(mediator.Object);

            await handler.Handle(new PublishAgentEventDomainEvent(SubAgentId, "Images", "edit it", "session-1", "image-edit", approvalRootAgentId: root),
                TestContext.Current.CancellationToken);

            mediator.Verify(x => x.Send(It.Is<IWkfloRequest<Result>>(c => ((PublishAgentEventCommand)c).ApprovalRootAgentId == root), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AgentInvoker_ForwardsTheApprovalRootOntoInvokeAgentCommand()
        {
            var mediator = new Mock<IWkfloMediator>();
            mediator.Setup(x => x.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SuccessResult<AgentInvocationResult>(new AgentInvocationResult()));
            var invoker = new AgentInvoker(new TenantContext(), new ChatContext(), new UserContext(), new AgentSharableContext(), WkfloMapperExtensions.CreateMapper(),
                mediator.Object, Mock.Of<IDateTimeProvider>(), NullLogger<AgentInvoker>.Instance);

            await invoker.InvokeAsync(new AgentInvocationRequest
            {
                AgentId = SubAgentId,
                SessionId = "session-1",
                Input = new AgentInvocationInput { Text = "edit it" },
                ApprovalRootAgentId = RootAgentId,
                TenantContext = new TenantContext(),
                ChatContext = new ChatContext(),
                UserContext = new UserContext(),
                AgentSharableContext = new AgentSharableContext()
            }, TestContext.Current.CancellationToken);

            mediator.Verify(x => x.Send(It.Is<IWkfloRequest<Result>>(c => ((InvokeAgentCommand)c).ApprovalRootAgentId == RootAgentId), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task InvokeAgentCommandHandler_PassesTheApprovalRootToTheRun()
        {
            var runnable = new Mock<IRunnableAISyncService>();
            var handler = new InvokeAgentCommandHandler(runnable.Object, Mock.Of<IDateTimeProvider>(), new ChatContext());

            await handler.Handle(new InvokeAgentCommand { Id = SubAgentId, SessionId = "session-1", Input = new AgentInvocationInput { Text = "edit it" }, ApprovalRootAgentId = RootAgentId },
                TestContext.Current.CancellationToken);

            runnable.Verify(x => x.RunRunnableAgentByIdAsync(SubAgentId, "edit it", It.IsAny<List<FileAttachment>>(), "session-1", It.IsAny<bool>(), It.IsAny<bool?>(), RootAgentId, It.IsAny<CancellationToken>()), Times.Once);
        }

        private static TenantAgent Agent(int id, string name, bool isActive = true) => new()
        {
            Id = id,
            Name = name,
            IsActive = isActive,
            Tenant = new Tenant { Name = "TestTenant" },
            AIModel = new AIModel { Id = 3, Provider = "openai", Model = "gpt-4o", Name = "model", Secret = new Secret { ApiKey = "test-key" } },
            AgentTools = new List<AgentTool>()
        };

        private static (RunnableAISyncService Service, Func<RunTextOptions> Seen) MockedService(List<AgentAllowedSpinUpAgent> allowlist = null, List<AgentChatSetting> settings = null)
        {
            RunTextOptions seen = null;
            var semanticKernel = new Mock<ISemanticKernelAgentService>();
            semanticKernel.Setup(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RunTextOptions, CancellationToken>((o, _) => seen = o)
                .ReturnsAsync(new AgentAnswer { MessageId = "m", Message = "Edited.", TokenUsage = new AITokenUsage() });

            var root = Agent(RootAgentId, "Assistant");
            root.EnableSpinUpAgents = true;
            var sub = Agent(SubAgentId, "Image editor");
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(new List<Agent> { root, sub }.BuildMockDbSet().Object);
            db.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns((settings ?? []).BuildMockDbSet().Object);
            db.Setup(x => x.AgentAllowedSpinUpAgents).Returns((allowlist ?? [new AgentAllowedSpinUpAgent { AgentId = RootAgentId, AllowedSpinUpAgentId = SubAgentId, AllowedSpinUpAgent = sub }])
                .BuildMockDbSet().Object);

            var service = new RunnableAISyncService(db.Object, WkfloMapperExtensions.CreateMapper(), semanticKernel.Object, Mock.Of<IChatNotificationService>(),
                Mock.Of<IDateTimeProvider>(), new TenantContext { TenantName = "TestTenant" });
            return (service, () => seen);
        }

        [Theory]
        [InlineData(RootAgentId, true)]
        [InlineData(null, false)]
        public async Task RunRunnableAgentById_HasApprovalsOnlyWithAnApprovalRoot(int? root, bool expectedApprovals)
        {
            var (service, seen) = MockedService();

            await service.RunRunnableAgentByIdAsync(SubAgentId, "edit it", [], "session-1", chainOfThought: false, rootIsWidget: null, approvalRootAgentId: root,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(expectedApprovals, seen().ApprovalsAvailable);
            Assert.Equal(root, seen().RootAgentId);
            Assert.Equal(SubAgentId, seen().Agent.Id);
        }

        // ---- the sub-agent's Ask produces a request attributed to it, in the root's chat ------------------

        [Fact]
        public async Task SubAgentAsk_StoresARequestWithTheRootAgent()
        {
            var state = new Mock<IToolApprovalStateService>();
            var notifications = new Mock<IChatNotificationService>();
            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", SubAgentId, "Image editor", null, null, Guid.NewGuid(),
                new Dictionary<int, string> { [ToolPermissionPolicyTests.HttpToolId] = "Image editing" }, new ToolApprovalChatState(), RootAgentId);
            var filter = new ToolApprovalAutoFunctionInvocationFilter(ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable: true),
                state.Object, Mock.Of<IToolApprovalExpiryScheduler>(), notifications.Object, new ChatContext { SessionId = "session-1" }, Mock.Of<IDateTimeProvider>(),
                runInfo, NullLogger.Instance);
            var function = KernelFunctionFactory.CreateFromMethod(() => "edited", "edit_image").Clone($"plugin_{ToolPermissionPolicyTests.HttpToolId}_HTTP");
            var context = new AutoFunctionInvocationContext(Kernel.CreateBuilder().Build(), function, new FunctionResult(function), new ChatHistory(),
                new ChatMessageContent(AuthorRole.Assistant, ""))
            { ToolCallId = "call-1", Arguments = new KernelArguments() };

            await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

            Assert.True(context.Terminate);
            Assert.Equal(SubAgentId, filter.RequestedApproval.AgentId);
            Assert.Equal(RootAgentId, filter.RequestedApproval.RootAgentId);
            notifications.Verify(x => x.SendToolApprovalRequestAsync("session-1", It.Is<AMAIS.WKFLO.Common.Models.SignalR.ToolApprovalRequestUpdate>(u => u.AgentName == "Image editor")), Times.Once);
        }

        // ---- decisions from the root chat ------------------------------------------------------------------

        [Theory]
        [InlineData(RootAgentId, ResultType.Ok)]
        [InlineData(SubAgentId, ResultType.Ok)]
        [InlineData(99, ResultType.NotFound)]
        public async Task Decision_AcceptedViaTheSubAgentOrItsRoot_RejectedViaAnUnrelatedAgent(int endpointAgentId, ResultType expected)
        {
            var record = ToolApprovalHistoryTests.Record();
            record.AgentId = SubAgentId;
            record.RootAgentId = RootAgentId;
            var state = new Mock<IToolApprovalStateService>();
            state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(record);
            state.Setup(x => x.TryDecideAsync("approval-1", "AllowOnce", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ToolApprovalDecisionOutcome.Decided, ToolApprovalHistoryTests.Record("Approved", "AllowOnce")));
            var processor = new ToolApprovalDecisionProcessor(state.Object, Mock.Of<IToolApprovalNotifier>(), Mock.Of<IToolApprovalResumeQueue>(),
                new TenantContext { TenantName = "TestTenant" }, new UserContext(), Mock.Of<IDateTimeProvider>());

            var result = await processor.DecideAsync(new DecideTenantToolApprovalCommand { Id = endpointAgentId, ApprovalId = "approval-1", SessionId = "session-1", Decision = "AllowOnce" },
                endpointAgentId, TestContext.Current.CancellationToken);

            Assert.Equal(expected, result.ResultType);
        }

        // ---- resume ----------------------------------------------------------------------------------------

        private static ToolApprovalRecord SubAgentRecord(string shareId = null)
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            record.TenantName = "TestTenant";
            record.AgentId = SubAgentId;
            record.RootAgentId = RootAgentId;
            record.ShareId = shareId;
            record.Chat = new ToolApprovalChatState { RootIsWidget = true };
            return record;
        }

        [Fact]
        public async Task Resume_RunsAsTheSubAgent_InTheSameSession_WithApprovalsAndTheRoot()
        {
            var (service, seen) = MockedService(settings: [new AgentChatSetting { Id = 1, ShareId = "share-1", AgentId = RootAgentId }]);

            var answer = await service.ResumeAfterToolApprovalAsync(SubAgentRecord("share-1"), TestContext.Current.CancellationToken);

            Assert.Equal("Edited.", answer);
            Assert.Equal(SubAgentId, seen().Agent.Id);
            Assert.Equal("session-1", seen().SessionId);
            Assert.Equal(RootAgentId, seen().RootAgentId);
            Assert.True(seen().ApprovalsAvailable);
            Assert.True(seen().RootIsWidget);
            Assert.NotNull(seen().ToolApprovalResume);
        }

        /// <summary>CanResumeAsync's queries against a real relational provider (SQLite in-memory).</summary>
        [Theory]
        [InlineData("ok", null, true)]
        [InlineData("ok", "root-share", true)]
        [InlineData("ok", "sub-agent-share", false)]   // the share must point at the root's chat
        [InlineData("ok", "revoked-share", false)]
        [InlineData("not-allowlisted", null, false)]
        [InlineData("sub-agent-inactive", null, false)]
        [InlineData("sub-agent-deleted", null, false)]
        [InlineData("root-deleted", null, false)]
        [InlineData("root-inactive", null, false)]
        [InlineData("root-hand-offs-off", null, false)]
        [InlineData("root-other-tenant", null, false)]
        [InlineData("published-root", null, true)]
        public async Task CanResume_ChecksRootShareAllowlistAndSubAgentState_OnARealProvider(string scenario, string shareId, bool expected)
        {
            await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var db = new MasterDataDbContext(new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(connection).Options, Mock.Of<IDateTimeProvider>());
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var tenant = new Tenant { Name = "TestTenant" };
            var otherTenant = new Tenant { Name = "OtherTenant" };
            db.Tenants.AddRange(tenant, otherTenant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            RunnableAgent root = scenario == "published-root" ? new PublishedTenantAgent() : new TenantAgent();
            root.TenantId = scenario == "root-other-tenant" ? otherTenant.Id : tenant.Id;
            root.Name = "Assistant";
            root.Description = "d";
            root.SystemPrompt = "p";
            root.IsActive = scenario != "root-inactive";
            root.IsDeleted = scenario == "root-deleted";
            root.EnableSpinUpAgents = scenario != "root-hand-offs-off";
            db.Agents.Add(root);
            var sub = new TenantAgent
            {
                TenantId = tenant.Id,
                Name = "Image editor",
                Description = "d",
                SystemPrompt = "p",
                IsActive = scenario != "sub-agent-inactive",
                IsDeleted = scenario == "sub-agent-deleted"
            };
            db.TenantAgents.Add(sub);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            if (scenario != "not-allowlisted")
                db.AgentAllowedSpinUpAgents.Add(new AgentAllowedSpinUpAgent { AgentId = root.Id, AllowedSpinUpAgentId = sub.Id });
            db.AgentChatSettings.AddRange(
                new AgentChatSetting { AgentId = root.Id, ShareId = "root-share" },
                new AgentChatSetting { AgentId = sub.Id, ShareId = "sub-agent-share" },
                new AgentChatSetting { AgentId = root.Id, ShareId = "revoked-share", IsDeleted = true });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.ChangeTracker.Clear();

            var record = SubAgentRecord(shareId);
            record.AgentId = sub.Id;
            record.RootAgentId = root.Id;
            var service = new RunnableAISyncService(db, WkfloMapperExtensions.CreateMapper(), Mock.Of<ISemanticKernelAgentService>(), Mock.Of<IChatNotificationService>(),
                Mock.Of<IDateTimeProvider>(), new TenantContext());

            Assert.Equal(expected, await service.CanResumeAsync(record, TestContext.Current.CancellationToken));
        }
    }
}
