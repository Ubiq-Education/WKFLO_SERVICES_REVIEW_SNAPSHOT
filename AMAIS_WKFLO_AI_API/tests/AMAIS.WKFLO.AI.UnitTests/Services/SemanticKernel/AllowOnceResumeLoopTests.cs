using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Bug 7722: "Allow once" asked again and again. The approved stored call ran before the turn had set the
    /// conversation on the chat context, so edit_image found no image, failed, and the model called it again (a new
    /// approval each time). Also guarded: the approved call repeated with the same arguments is not asked for again.
    /// </summary>
    public class AllowOnceResumeLoopTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        /// <summary>Like edit_image: needs the conversation (its source image) from the chat context.</summary>
        public sealed class ConversationReadingPlugin(ChatContext chatContext)
        {
            [KernelFunction("edit_image"), Description("Edits the latest image")]
            public string EditImage(string instruction) =>
                chatContext.CurrentChatHistory == null
                    ? "<p>Image editing failed: No image found in conversation history.</p>"
                    : $"Image editing completed successfully ({instruction}, {chatContext.CurrentChatHistory.Count} messages).";
        }

        private static SemanticKernelAgentService Service(ChatContext chatContext) =>
            new(Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext(), chatContext, new UserContext(), Mock.Of<IBlobStorageProvider>(), Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(), Mock.Of<IMasterDataDbContext>(), Mock.Of<IContactFormPluginLogService>(),
                toolApprovalStateService: Mock.Of<IToolApprovalStateService>(), toolApprovalExpiryScheduler: Mock.Of<IToolApprovalExpiryScheduler>());

        private static ToolApprovalRecord ApprovedEditImage(int? rootAgentId, string argumentsJson = "{\"instruction\":\"add a bird\"}")
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            record.ToolId = 7;
            record.IsMcp = false;
            record.ToolName = "Image editing";
            record.PluginName = "plugin_7_Image_Generation";
            record.FunctionName = "edit_image";
            record.ArgumentsJson = argumentsJson;
            record.RootAgentId = rootAgentId;
            return record;
        }

        [Theory]
        [InlineData(null)] // root agent
        [InlineData(10)]   // hand-off sub-agent
        public async Task AllowOnce_StoredCallSeesTheConversation_AndItsResultReplacesThePlaceholderInTheCallResultPair(int? rootAgentId)
        {
            var chatContext = new ChatContext { SessionId = "session-1" };
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromObject(new ConversationReadingPlugin(chatContext), "plugin_7_Image_Generation");
            var agent = new TenantAgent
            {
                Id = 5,
                AgentTools = [new AgentTool { AgentId = 5, ToolId = 7, ToolPermission = "Ask", Tool = new TenantTool { Id = 7, Type = "Image_Generation", PermissionsEnabled = true, IsActive = true } }]
            };
            var record = ApprovedEditImage(rootAgentId);
            var history = new ChatHistory();
            history.AddUserMessage("Add a bird to my photo");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent("edit_image", "plugin_7_Image_Generation", record.CallId)]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("edit_image", "plugin_7_Image_Generation", record.CallId, ToolApprovalHistory.AwaitingApprovalResult)]));

            var cue = await Service(chatContext).ApplyToolApprovalDecisionAsync(kernel, ToolPermissionPolicy.Build(agent, approvalsAvailable: true), history, record,
                TestContext.Current.CancellationToken);

            // Assistant tool call + tool result with the same call id; the result is the real outcome, not the placeholder.
            var call = Assert.Single(history[1].Items.OfType<FunctionCallContent>());
            var result = Assert.Single(history[2].Items.OfType<FunctionResultContent>());
            Assert.Equal(AuthorRole.Assistant, history[1].Role);
            Assert.Equal(AuthorRole.Tool, history[2].Role);
            Assert.Equal(call.Id, result.CallId);
            Assert.Equal("Image editing completed successfully (add a bird, 3 messages).", result.Result);
            Assert.DoesNotContain(history.SelectMany(m => m.Items).OfType<FunctionResultContent>(), r => r.Result?.ToString() == ToolApprovalHistory.AwaitingApprovalResult);
            Assert.DoesNotContain("Tool result:", cue); // outcome is in the history, not repeated in the cue
        }

        // ---- loop guard in the resumed turn ------------------------------------------------------------------

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly ChatContext _chatContext = new() { SessionId = "session-1" };
        private readonly Kernel _kernel = Kernel.CreateBuilder().Build();

        private ToolApprovalAutoFunctionInvocationFilter Filter(ToolApprovalRecord alreadyRun)
        {
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable: true);
            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", 100, "Support Agent", "share-1", null, Guid.NewGuid(),
                new Dictionary<int, string> { [2] = "Weather" }, RootAgentId: alreadyRun?.RootAgentId, AlreadyRunCall: alreadyRun);
            return new ToolApprovalAutoFunctionInvocationFilter(policy, _state.Object, Mock.Of<IToolApprovalExpiryScheduler>(), Mock.Of<IChatNotificationService>(),
                _chatContext, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), runInfo, NullLogger.Instance);
        }

        private AutoFunctionInvocationContext Context(int id)
        {
            var bound = KernelFunctionFactory.CreateFromMethod(() => "ran", "call_http", "Calls the API").Clone("plugin_2_HTTP");
            return new AutoFunctionInvocationContext(_kernel, bound, new FunctionResult(bound), new ChatHistory(), new ChatMessageContent(AuthorRole.Assistant, ""))
            {
                Arguments = new KernelArguments { ["id"] = id },
                ToolCallId = "call-2"
            };
        }

        private static ToolApprovalRecord ApprovedHttpCall(int? rootAgentId, string decision = "AllowOnce")
        {
            var record = ToolApprovalHistoryTests.Record("Approved", decision);
            record.ToolId = 2;
            record.IsMcp = false;
            record.PluginName = "plugin_2_HTTP";
            record.FunctionName = "call_http";
            record.ArgumentsJson = "{\"id\":42}";
            record.RootAgentId = rootAgentId;
            return record;
        }

        [Theory]
        [InlineData(null)]
        [InlineData(10)]
        public async Task AllowOnce_SameCallAgain_IsNotAskedForOrRunAgain(int? rootAgentId)
        {
            var filter = Filter(ApprovedHttpCall(rootAgentId));
            var context = Context(42);
            var nextCalled = false;

            await filter.OnAutoFunctionInvocationAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

            Assert.False(nextCalled);
            Assert.False(context.Terminate); // the turn goes on: the model answers with the result it already has
            Assert.Equal(ToolApprovalHistory.AlreadyDoneResult, context.Result.ToString());
            Assert.Null(filter.RequestedApproval);
            Assert.False(_chatContext.ToolApprovalRequestedThisTurn);
            _state.Verify(x => x.SaveNewAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(10)]
        public async Task AllowForConversation_SameCallAgain_RunsThroughTheGrant(int? rootAgentId)
        {
            _state.Setup(x => x.HasConversationGrantAsync("session-1", "2", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var filter = Filter(ApprovedHttpCall(rootAgentId, "AllowForConversation"));
            var context = Context(42);
            var nextCalled = false;

            await filter.OnAutoFunctionInvocationAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

            Assert.True(nextCalled);
            Assert.NotEqual(ToolApprovalHistory.AlreadyDoneResult, context.Result?.ToString());
            Assert.Null(filter.RequestedApproval);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(10)]
        public async Task SameFunctionWithOtherArguments_AsksNormally(int? rootAgentId)
        {
            var filter = Filter(ApprovedHttpCall(rootAgentId));
            var context = Context(43);

            await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

            Assert.True(context.Terminate);
            Assert.Equal(ToolApprovalHistory.AwaitingApprovalResult, context.Result.ToString());
            Assert.Equal("{\"id\":43}", filter.RequestedApproval?.ArgumentsJson);
            _state.Verify(x => x.SaveNewAsync(It.IsAny<ToolApprovalRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(10, true)]    // hand-off sub-agent
        [InlineData(null, false)] // root agent: no activity update
        public async Task NewApproval_SubAgentSendsAwaitingApprovalBeforeTheCard(int? rootAgentId, bool expectActivity)
        {
            var calls = new List<string>();
            var notifications = new Mock<IChatNotificationService>();
            notifications.Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, int?, string, string, string, string, string, string>((_, agentId, agentName, status, icon, action, _, _) =>
                    calls.Add($"activity:{status}:{agentId}:{agentName}:{icon}:{action}"))
                .Returns(Task.CompletedTask);
            notifications.Setup(x => x.SendToolApprovalRequestAsync(It.IsAny<string>(), It.IsAny<AMAIS.WKFLO.Common.Models.SignalR.ToolApprovalRequestUpdate>()))
                .Callback(() => calls.Add("card"))
                .Returns(Task.CompletedTask);
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(), approvalsAvailable: true);
            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", 100, "Support Agent", "share-1", "https://example.test/icon.png", Guid.NewGuid(),
                new Dictionary<int, string> { [2] = "Weather" }, new ToolApprovalChatState { SubagentActivityAction = "image-edit" }, rootAgentId);
            var filter = new ToolApprovalAutoFunctionInvocationFilter(policy, _state.Object, Mock.Of<IToolApprovalExpiryScheduler>(), notifications.Object,
                _chatContext, Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), runInfo, NullLogger.Instance);

            await filter.OnAutoFunctionInvocationAsync(Context(42), _ => Task.CompletedTask);

            Assert.Equal(expectActivity
                ? ["activity:awaiting_approval:100:Support Agent:https://example.test/icon.png:image-edit", "card"]
                : ["card"], calls);
        }

        [Fact]
        public async Task OrdinaryTurn_AsksNormally()
        {
            var filter = Filter(alreadyRun: null);
            var context = Context(42);

            await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);

            Assert.NotNull(filter.RequestedApproval);
        }

        [Theory]
        [InlineData("{\"id\":42,\"note\":null}", "call_http", true)]   // a null argument is the same as leaving it out
        [InlineData("{\"id\":42.0}", "call_http", true)]               // same JSON number
        [InlineData("{\"id\":42,\"note\":\"x\"}", "call_http", false)]
        [InlineData("{\"id\":42}", "other_function", false)]
        [InlineData("not json", "call_http", false)]
        public void IsAlreadyRunCall_ComparesFunctionAndArguments(string storedArguments, string functionName, bool expected)
        {
            var record = ApprovedHttpCall(null);
            record.ArgumentsJson = storedArguments;

            Assert.Equal(expected, ToolApprovalAutoFunctionInvocationFilter.IsAlreadyRunCall(record, "plugin_2_HTTP", functionName, new KernelArguments { ["id"] = 42 }));
        }
    }
}
