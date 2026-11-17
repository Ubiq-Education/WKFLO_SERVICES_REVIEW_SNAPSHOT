using AMAIS.WKFLO.AI.UnitTests.Domain.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    /// <summary>
    /// WI 7726: the approval request/card carries the MCP function's friendly display values (from the run's tool
    /// copy) next to the unchanged functionName/functionDescription.
    /// </summary>
    public class ToolApprovalFunctionDisplayTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        private readonly Mock<IToolApprovalStateService> _state = new();
        private readonly Mock<IChatNotificationService> _notifications = new();
        private readonly ChatContext _chatContext = new() { SessionId = "session-1" };
        private readonly Kernel _kernel = Kernel.CreateBuilder().Build();

        private ToolApprovalAutoFunctionInvocationFilter CreateFilter(string discoveredFunctions)
        {
            var agent = ToolPermissionPolicyTests.BuildAgent();
            var mcpTool = agent.AgentTools.First().Tool;
            mcpTool.DiscoveredFunctions = discoveredFunctions;

            var runInfo = new ToolApprovalRunInfo("TestTenant", "user-1", "session-1", 100, "Support Agent", null, null, Guid.NewGuid(),
                new Dictionary<int, string> { [mcpTool.Id] = "Docs" },
                McpFunctions: ToolApprovalRunInfo.IndexMcpFunctions(agent.AgentTools.Select(at => at.Tool)));

            return new ToolApprovalAutoFunctionInvocationFilter(ToolPermissionPolicy.Build(agent, approvalsAvailable: true), _state.Object,
                Mock.Of<IToolApprovalExpiryScheduler>(), _notifications.Object, _chatContext,
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), runInfo, NullLogger.Instance);
        }

        private async Task<ToolApprovalRecord> AskSearchAsync(ToolApprovalAutoFunctionInvocationFilter filter)
        {
            var bound = KernelFunctionFactory.CreateFromMethod(() => "ran", "search", "Raw server description").Clone("plugin_1_MCP");
            var context = new AutoFunctionInvocationContext(_kernel, bound, new FunctionResult(bound), new ChatHistory(), new ChatMessageContent(AuthorRole.Assistant, ""))
            {
                Arguments = new KernelArguments(),
                ToolCallId = "call-1"
            };

            await filter.OnAutoFunctionInvocationAsync(context, _ => Task.CompletedTask);
            return Assert.IsType<ToolApprovalRecord>(filter.RequestedApproval);
        }

        private static ToolApprovalCardData ReadPersistedCard(ChatContext chatContext) =>
            JsonConvert.DeserializeObject<ToolApprovalCardData>((string)Assert.Single(chatContext.AssistantPostMessagesToBeProcessed).Metadata[CommonMetadataConsts.Data]);

        [Fact]
        public async Task FriendlyValuesSet_AreOnRecordSignalRAndCard_OriginalsUnchanged()
        {
            var filter = CreateFilter(ToolPermissionJson.SerializeDiscoveredFunctions(
                [new McpFunctionDescriptor { Name = "search", Description = "Raw server description", DisplayName = "Search content", DisplayDescription = "Finds content" }]));

            var record = await AskSearchAsync(filter);

            Assert.Equal("search", record.FunctionName);
            Assert.Equal("Raw server description", record.FunctionDescription);
            Assert.Equal("Search content", record.FunctionDisplayName);
            Assert.Equal("Finds content", record.FunctionDisplayDescription);

            _notifications.Verify(x => x.SendToolApprovalRequestAsync("session-1", It.Is<ToolApprovalRequestUpdate>(u =>
                u.FunctionName == "search"
                && u.FunctionDescription == "Raw server description"
                && u.FunctionDisplayName == "Search content"
                && u.FunctionDisplayDescription == "Finds content")), Times.Once);

            var card = ReadPersistedCard(_chatContext);
            Assert.Equal("search", card.FunctionName);
            Assert.Equal("Search content", card.FunctionDisplayName);
            Assert.Equal("Finds content", card.FunctionDisplayDescription);
            // The history text the model sees keeps the raw function name (display values are display-only).
            Assert.Equal("Support Agent wants to use search from Docs.", Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed).Content);
        }

        [Fact]
        public async Task FriendlyValuesNotSet_AreNull()
        {
            var filter = CreateFilter(ToolPermissionJson.SerializeDiscoveredFunctions([new McpFunctionDescriptor { Name = "search", Description = "Raw server description" }]));

            var record = await AskSearchAsync(filter);

            Assert.Null(record.FunctionDisplayName);
            Assert.Null(record.FunctionDisplayDescription);
            var card = ReadPersistedCard(_chatContext);
            Assert.Null(card.FunctionDisplayName);
            Assert.Null(card.FunctionDisplayDescription);
            Assert.Equal("Support Agent wants to use search from Docs.", Assert.Single(_chatContext.AssistantPostMessagesToBeProcessed).Content);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("[{\"name\":\"other\",\"displayName\":\"Other\"}]")]
        public async Task FunctionNotInSnapshot_DisplayValuesAreNull(string discoveredFunctions)
        {
            var filter = CreateFilter(discoveredFunctions);

            var record = await AskSearchAsync(filter);

            Assert.Equal("search", record.FunctionName);
            Assert.Null(record.FunctionDisplayName);
            Assert.Null(record.FunctionDisplayDescription);
        }

        [Fact]
        public void CardPersistedBefore7726_DeserializesWithNullDisplayValues()
        {
            const string oldCard = "{\"chatId\":\"s\",\"sessionId\":\"s\",\"approvalId\":\"a-1\",\"agentId\":100,\"agentName\":\"Support Agent\","
                + "\"toolName\":\"Docs\",\"functionName\":\"search\",\"functionDescription\":\"Raw\",\"argumentsJson\":\"{}\","
                + "\"expiresAtUtc\":\"2026-05-01T10:10:00+00:00\",\"status\":\"Pending\"}";
            var message = new ChatMessageContent(AuthorRole.Assistant, "x", metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.FormType, ToolApprovalConsts.FormType },
                { CommonMetadataConsts.Data, oldCard }
            });

            var card = ToolApprovalHistory.ReadCard(message);

            Assert.NotNull(card);
            Assert.Equal("a-1", card.ApprovalId);
            Assert.Equal("search", card.FunctionName);
            Assert.Null(card.FunctionDisplayName);
            Assert.Null(card.FunctionDisplayDescription);
        }

        [Fact]
        public void RecordStoredBefore7726_DeserializesWithNullDisplayValues()
        {
            var record = System.Text.Json.JsonSerializer.Deserialize<ToolApprovalRecord>("{\"ApprovalId\":\"a-1\",\"FunctionName\":\"search\",\"FunctionDescription\":\"Raw\"}");

            Assert.Equal("search", record.FunctionName);
            Assert.Null(record.FunctionDisplayName);
            Assert.Null(record.FunctionDisplayDescription);
        }

        [Fact]
        public void IndexMcpFunctions_OnlyIndexesMcpTools()
        {
            var index = ToolApprovalRunInfo.IndexMcpFunctions(
            [
                new TenantTool { Id = 1, Type = "MCP", DiscoveredFunctions = "[{\"name\":\"search\",\"displayName\":\"Search content\"}]" },
                new TenantTool { Id = 2, Type = "HTTP", DiscoveredFunctions = "[{\"name\":\"search\"}]" },
                null
            ]);

            Assert.Equal("Search content", index[1]["search"].DisplayName);
            Assert.False(index.ContainsKey(2));
        }
    }
}
