using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    /// <summary>WI 7575: "Ask for permission" interception of model-initiated calls.</summary>
    public class ToolApprovalAutoFunctionInvocationFilterTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IToolApprovalExpiryScheduler> _scheduler = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly ChatContext _chatContext = new() { SessionId = "session-1" };
        private readonly Kernel _kernel = Kernel.CreateBuilder().Build();
        private readonly ToolApprovalAutoFunctionInvocationFilter _filter;

        public ToolApprovalAutoFunctionInvocationFilterTests()
        {
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable: true);
            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", 100, "Support Agent", "share-1", "https://static.example.com/icon.png",
                Guid.Parse("11111111-1111-1111-1111-111111111111"), new Dictionary<int, string> { [1] = "Docs", [2] = "Weather" });
            _filter = new ToolApprovalAutoFunctionInvocationFilter(policy, _state.Object, _scheduler.Object, _notifications.Object, _chatContext,
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), runInfo, NullLogger.Instance);
        }

        private AutoFunctionInvocationContext Context(string plugin, string function, string callId = "call-1")
        {
            var bound = KernelFunctionFactory.CreateFromMethod(() => "ran", function, $"{function} description").Clone(plugin);
            return new AutoFunctionInvocationContext(_kernel, bound, new FunctionResult(bound), new ChatHistory(), new ChatMessageContent(AuthorRole.Assistant, ""))
            {
                Arguments = new KernelArguments { ["id"] = 42 },
                ToolCallId = callId
            };
        }

        [Fact]
        public async Task AskWithoutGrant_StoresPendingCall_NotifiesAndTerminates_WithoutRunning()
        {
            var context = Context("plugin_2_HTTP", "call_http");
            var nextCalled = false;

            await _filter.OnAutoFunctionInvocationAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

            Assert.False(nextCalled);
            Assert.True(context.Terminate);
            Assert.Equal(ToolApprovalHistory.AwaitingApprovalResult, context.Result.ToString());
            Assert.True(_chatContext.ToolApprovalRequestedThisTurn);

            var record = _filter.RequestedApproval;
            Assert.NotNull(record);
            Assert.Equal("Pending", record.Status);
            Assert.Equal(2, record.ToolId);
            Assert.Equal("Weather", record.ToolName);
            Assert.False(record.IsMcp);
            Assert.Equal("call_http", record.FunctionName);
            Assert.Equal("{\"id\":42}", record.ArgumentsJson);
            Assert.Equal("call-1", record.CallId);
            Assert.Equal("share-1", record.ShareId);
            Assert.Equal("user-1", record.UserId);
            Assert.Equal(Now.AddMinutes(10), record.ExpiresAt);

            _state.Verify(x => x.SaveNewAsync(record, It.IsAny<CancellationToken>()), Times.Once);
            _scheduler.Verify(x => x.ScheduleAsync(It.Is<ToolApprovalExpiryWorkflowInput>(i => i.ApprovalId == record.ApprovalId)), Times.Once);
            _notifications.Verify(x => x.SendToolApprovalRequestAsync("session-1", It.Is<ToolApprovalRequestUpdate>(u =>
                u.ApprovalId == record.ApprovalId && u.FunctionName == "call_http" && u.ToolName == "Weather" && u.AgentName == "Support Agent")), Times.Once);

            var card = Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed);
            Assert.Equal("ToolApproval", card.Metadata[CommonMetadataConsts.FormType]);
        }

        [Fact]
        public async Task AskWithConversationGrant_Runs()
        {
            _state.Setup(x => x.HasConversationGrantAsync("session-1", "1:search", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var context = Context("plugin_1_MCP", "search");
            var nextCalled = false;

            await _filter.OnAutoFunctionInvocationAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

            Assert.True(nextCalled);
            Assert.Null(_filter.RequestedApproval);
            _state.Verify(x => x.SaveNewAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("plugin_1_MCP", "read")]
        [InlineData("plugin_spin_up_agent_event_publisher", "publish_agent_event")]
        public async Task NotAsk_RunsUntouched(string plugin, string function)
        {
            var context = Context(plugin, function);
            var nextCalled = false;

            await _filter.OnAutoFunctionInvocationAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

            Assert.True(nextCalled);
            Assert.False(context.Terminate);
        }

        [Fact]
        public async Task SecondAskInSameTurn_IsNotRunAndNotStored()
        {
            await _filter.OnAutoFunctionInvocationAsync(Context("plugin_2_HTTP", "call_http"), _ => Task.CompletedTask);
            var second = Context("plugin_1_MCP", "search", "call-2");

            await _filter.OnAutoFunctionInvocationAsync(second, _ => Task.CompletedTask);

            Assert.Equal(ToolApprovalHistory.NotRunResult, second.Result.ToString());
            Assert.True(second.Terminate);
            _state.Verify(x => x.SaveNewAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExpirySchedulingFailure_IsNotFatal()
        {
            _scheduler.Setup(x => x.ScheduleAsync(It.IsAny<ToolApprovalExpiryWorkflowInput>())).ThrowsAsync(new InvalidOperationException("sidecar down"));
            var context = Context("plugin_2_HTTP", "call_http");

            await _filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

            Assert.True(context.Terminate);
            Assert.NotNull(_filter.RequestedApproval);
        }
    }
}
