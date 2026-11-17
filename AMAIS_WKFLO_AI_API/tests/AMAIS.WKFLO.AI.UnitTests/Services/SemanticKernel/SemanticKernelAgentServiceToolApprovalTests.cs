using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>WI 7575: the approval parts of a text run - resume of the stored call, card sync, turn finalization.</summary>
    public class SemanticKernelAgentServiceToolApprovalTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 3, 0, TimeSpan.Zero);

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly SemanticKernelAgentService _service;

        public SemanticKernelAgentServiceToolApprovalTests()
        {
            _service = new SemanticKernelAgentService(
                Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext(), new ChatContext(), new UserContext(), Mock.Of<IBlobStorageProvider>(), Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), Mock.Of<IMasterDataDbContext>(), Mock.Of<IContactFormPluginLogService>(),
                toolApprovalStateService: _state.Object, toolApprovalExpiryScheduler: Mock.Of<IToolApprovalExpiryScheduler>());
        }

        private static ChatHistory HistoryWithCard(ToolApprovalRecord pending)
        {
            var history = new ChatHistory();
            history.AddUserMessage("Delete document 42");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent(pending.FunctionName, pending.PluginName, pending.CallId)]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent(pending.FunctionName, pending.PluginName, pending.CallId, ToolApprovalHistory.AwaitingApprovalResult)]));
            history.Add(ToolApprovalHistory.CreateCardMessage(pending, Now));
            return history;
        }

        private static (Kernel Kernel, Func<int> Calls, ToolPermissionPolicy Policy) KernelWithMcpFunction()
        {
            var calls = 0;
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions("plugin_1_MCP", [
                KernelFunctionFactory.CreateFromMethod((string id) => { calls++; return $"deleted {id}"; }, "delete")
            ]);
            var agent = ToolPermissionPolicyTests.BuildAgent(mcpFunctionPermissions: "{\"delete\":\"Ask\"}");
            return (kernel, () => calls, ToolPermissionPolicy.Build(agent, approvalsAvailable: true));
        }

        private static ToolApprovalRecord McpRecord(string status, string decision)
        {
            var record = ToolApprovalHistoryTests.Record(status, decision);
            record.ToolId = 1;
            record.PluginName = "plugin_1_MCP";
            record.FunctionName = "delete";
            record.ArgumentsJson = "{\"id\":\"42\"}";
            return record;
        }

        [Fact]
        public async Task ApplyDecision_Approved_RunsStoredCallAndReplacesPlaceholder()
        {
            var (kernel, calls, policy) = KernelWithMcpFunction();
            var record = McpRecord("Approved", "AllowOnce");
            var history = HistoryWithCard(McpRecord("Pending", null));

            var cue = await _service.ApplyToolApprovalDecisionAsync(kernel, policy, history, record, TestContext.Current.CancellationToken);

            Assert.Equal(1, calls());
            Assert.Equal("deleted 42", ((FunctionResultContent)history[2].Items[0]).Result);
            Assert.Contains("approved", cue);
            Assert.DoesNotContain("Tool result", cue);
            Assert.Empty(ToolApprovalHistory.GetPendingCardApprovalIds(history));
        }

        [Fact]
        public async Task ApplyDecision_Approved_NonStringResult_IsStoredAsJsonNotTypeName()
        {
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions("plugin_1_MCP", [
                KernelFunctionFactory.CreateFromMethod((string id) => new List<ApprovedItem> { new() { Message = $"deleted {id}" } }, "delete")
            ]);
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(mcpFunctionPermissions: "{\"delete\":\"Ask\"}"), approvalsAvailable: true);
            var history = HistoryWithCard(McpRecord("Pending", null));

            await _service.ApplyToolApprovalDecisionAsync(kernel, policy, history, McpRecord("Approved", "AllowOnce"), TestContext.Current.CancellationToken);

            var stored = (string)((FunctionResultContent)history[2].Items[0]).Result;
            Assert.DoesNotContain("System.Collections.Generic", stored);
            var items = JsonSerializer.Deserialize<List<ApprovedItem>>(stored);
            Assert.Equal("deleted 42", Assert.Single(items).Message);
        }

        [Fact]
        public void FormatApprovedResult_KeepsStringsAndHandlesNull()
        {
            var function = KernelFunctionFactory.CreateFromMethod(() => "x", "f");

            Assert.Equal("plain text", SemanticKernelAgentService.FormatApprovedResult(new FunctionResult(function, "plain text")));
            Assert.Equal(string.Empty, SemanticKernelAgentService.FormatApprovedResult(new FunctionResult(function, (object)null)));
            Assert.Equal(string.Empty, SemanticKernelAgentService.FormatApprovedResult(null));
        }

        public sealed class ApprovedItem
        {
            public string Message { get; set; }
        }

        [Fact]
        public async Task ApplyDecision_Approved_CallThrows_StoresFixedErrorWithoutExceptionDetails()
        {
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions("plugin_1_MCP", [
                KernelFunctionFactory.CreateFromMethod(
                    new Func<string, string>(id => throw new InvalidOperationException("Connection refused: https://internal-host.example:8443/secret-path")),
                    "delete")
            ]);
            var policy = ToolPermissionPolicy.Build(ToolPermissionPolicyTests.BuildAgent(mcpFunctionPermissions: "{\"delete\":\"Ask\"}"), approvalsAvailable: true);
            var history = HistoryWithCard(McpRecord("Pending", null));

            var cue = await _service.ApplyToolApprovalDecisionAsync(kernel, policy, history, McpRecord("Approved", "AllowOnce"), TestContext.Current.CancellationToken);

            var stored = (string)((FunctionResultContent)history[2].Items[0]).Result;
            Assert.Equal(ToolApprovalHistory.FailedResult, stored);
            Assert.DoesNotContain("internal-host", stored);
            Assert.DoesNotContain("internal-host", cue);
        }

        [Fact]
        public async Task ApplyDecision_Denied_DoesNotRun()
        {
            var (kernel, calls, policy) = KernelWithMcpFunction();
            var history = HistoryWithCard(McpRecord("Pending", null));

            var cue = await _service.ApplyToolApprovalDecisionAsync(kernel, policy, history, McpRecord("Denied", "Deny"), TestContext.Current.CancellationToken);

            Assert.Equal(0, calls());
            Assert.Equal(ToolApprovalHistory.DeniedResult, ((FunctionResultContent)history[2].Items[0]).Result);
            Assert.Contains("did not allow", cue);
        }

        [Fact]
        public async Task ApplyDecision_FunctionNoLongerRegistered_ReportsUnavailable()
        {
            var (_, _, policy) = KernelWithMcpFunction();
            var history = HistoryWithCard(McpRecord("Pending", null));

            await _service.ApplyToolApprovalDecisionAsync(Kernel.CreateBuilder().Build(), policy, history, McpRecord("Approved", "AllowOnce"), TestContext.Current.CancellationToken);

            Assert.Equal(ToolApprovalHistory.UnavailableResult, ((FunctionResultContent)history[2].Items[0]).Result);
        }

        [Fact]
        public async Task ApplyDecision_NoPlaceholderInHistory_PutsResultInCue()
        {
            var (kernel, _, policy) = KernelWithMcpFunction();

            var cue = await _service.ApplyToolApprovalDecisionAsync(kernel, policy, new ChatHistory(), McpRecord("Approved", "AllowOnce"), TestContext.Current.CancellationToken);

            Assert.Contains("Tool result: deleted 42", cue);
        }

        [Fact]
        public async Task SyncCards_ExpiresOverdueApproval_AndUpdatesCardAndPlaceholder()
        {
            var pending = McpRecord("Pending", null);
            pending.ExpiresAt = Now.AddMinutes(-1);
            var history = HistoryWithCard(pending);
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(pending);
            _state.Setup(x => x.TryExpireAsync("approval-1", Now, It.IsAny<CancellationToken>())).ReturnsAsync(McpRecord("Expired", "Expired"));

            await _service.SyncToolApprovalCardsAsync(history, TestContext.Current.CancellationToken);

            Assert.Empty(ToolApprovalHistory.GetPendingCardApprovalIds(history));
            Assert.Equal(ToolApprovalHistory.ExpiredResult, ((FunctionResultContent)history[2].Items[0]).Result);
        }

        [Fact]
        public async Task SyncCards_StillPending_LeavesCard()
        {
            var pending = McpRecord("Pending", null);
            var history = HistoryWithCard(pending);
            _state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(pending);

            await _service.SyncToolApprovalCardsAsync(history, TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "approval-1" }, ToolApprovalHistory.GetPendingCardApprovalIds(history));
        }

        [Fact]
        public void FinalizeTurn_AnswersDanglingCalls_AndEndsWithTaggedNotice()
        {
            var history = new ChatHistory();
            history.AddUserMessage("Delete document 42 and search");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent("delete", "plugin_1_MCP", "call-1"), new FunctionCallContent("search", "plugin_1_MCP", "call-2")]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("delete", "plugin_1_MCP", "call-1", ToolApprovalHistory.AwaitingApprovalResult)]));

            var notice = SemanticKernelAgentService.FinalizeToolApprovalTurn(history, McpRecord("Pending", null), string.Empty, "msg-1", Now);

            Assert.Equal("I need your permission to use delete from Docs before I can continue.", notice);
            Assert.Equal(5, history.Count);
            Assert.Equal(AuthorRole.Tool, history[3].Role);
            var last = history[^1];
            Assert.Equal(AuthorRole.Assistant, last.Role);
            Assert.Equal(notice, last.Content);
            Assert.Equal(true, last.Metadata[CommonMetadataConsts.IsToolApprovalNotice]);
            Assert.Equal("msg-1", last.Metadata[CommonMetadataConsts.MessageId]);
        }

        [Fact]
        public void BuildArguments_KeepsStringsAndPassesOtherValuesAsJson()
        {
            var arguments = SemanticKernelAgentService.BuildArguments("{\"q\":\"docs\",\"limit\":5,\"filters\":{\"a\":true},\"none\":null}");

            Assert.Equal("docs", arguments["q"]);
            Assert.Equal(5, ((JsonElement)arguments["limit"]).GetInt32());
            Assert.True(((JsonElement)arguments["filters"]).GetProperty("a").GetBoolean());
            Assert.Null(arguments["none"]);
            Assert.Empty(SemanticKernelAgentService.BuildArguments("not json"));
        }
    }
}
