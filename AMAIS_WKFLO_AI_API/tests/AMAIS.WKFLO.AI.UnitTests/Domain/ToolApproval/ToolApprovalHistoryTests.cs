using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval
{
    /// <summary>WI 7575: pure chat-history operations of the approval flow.</summary>
    public class ToolApprovalHistoryTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        internal static ToolApprovalRecord Record(string status = "Pending", string decision = null) => new()
        {
            ApprovalId = "approval-1",
            TenantName = "TestTenant",
            SessionId = "session-1",
            AgentId = 5,
            AgentName = "Support Agent",
            ToolId = 7,
            ToolName = "Docs",
            IsMcp = true,
            PluginName = "plugin_7_MCP",
            FunctionName = "delete_document",
            FunctionDescription = "Deletes a document",
            ArgumentsJson = "{\"id\":42}",
            CallId = "call-1",
            CardMessageId = "card-1",
            Status = status,
            Decision = decision,
            CreatedAt = Now,
            ExpiresAt = Now.AddMinutes(10)
        };

        private static ChatHistory HistoryWithPendingCall()
        {
            var history = new ChatHistory();
            history.AddUserMessage("Delete document 42");
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [
                new FunctionCallContent("delete_document", "plugin_7_MCP", "call-1"),
                new FunctionCallContent("search", "plugin_7_MCP", "call-2")
            ]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("delete_document", "plugin_7_MCP", "call-1", ToolApprovalHistory.AwaitingApprovalResult)]));
            return history;
        }

        [Fact]
        public void CreateCardMessage_IsFormsChannelToolApprovalWithCamelCaseData()
        {
            var message = ToolApprovalHistory.CreateCardMessage(Record(), Now);

            Assert.Equal(AuthorRole.Assistant, message.Role);
            Assert.Equal("Support Agent wants to use delete_document from Docs.", message.Content);
            Assert.Equal(ChannelMessageType.Forms, message.Metadata[CommonMetadataConsts.ChannelMessageType]);
            Assert.Equal("ToolApproval", message.Metadata[CommonMetadataConsts.FormType]);
            Assert.Equal("card-1", message.Metadata[CommonMetadataConsts.MessageId]);

            var data = JObject.Parse((string)message.Metadata[CommonMetadataConsts.Data]);
            Assert.Equal("approval-1", (string)data["approvalId"]);
            Assert.Equal("session-1", (string)data["chatId"]);
            Assert.Equal("Pending", (string)data["status"]);
            Assert.Equal("{\"id\":42}", (string)data["argumentsJson"]);
            Assert.Equal("delete_document", (string)data["functionName"]);
            Assert.False(data.ContainsKey("decision"));
        }

        [Fact]
        public void TryUpdateCard_WritesCurrentStatus_AlsoWhenMetadataCameBackFromJson()
        {
            var history = new ChatHistory();
            var card = ToolApprovalHistory.CreateCardMessage(Record(), Now);
            // Simulate the blob round-trip: metadata values come back as JsonElement.
            var roundTripped = card.Metadata.ToDictionary(kv => kv.Key, kv => (object)JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(kv.Value)).RootElement.Clone());
            history.Add(new ChatMessageContent(AuthorRole.Assistant, card.Content, metadata: roundTripped));

            Assert.Equal(new[] { "approval-1" }, ToolApprovalHistory.GetPendingCardApprovalIds(history));

            var updated = ToolApprovalHistory.TryUpdateCard(history, Record("Approved", "AllowOnce"));

            Assert.True(updated);
            Assert.Empty(ToolApprovalHistory.GetPendingCardApprovalIds(history));
            var data = JsonConvert.DeserializeObject<ToolApprovalCardData>((string)history[0].Metadata[CommonMetadataConsts.Data]);
            Assert.Equal("Approved", data.Status);
            Assert.Equal("AllowOnce", data.Decision);
            Assert.Equal(card.Content, history[0].Content);
        }

        [Fact]
        public void TryUpdateCard_UnknownApproval_ReturnsFalse()
        {
            var history = HistoryWithPendingCall();

            Assert.False(ToolApprovalHistory.TryUpdateCard(history, Record()));
        }

        [Fact]
        public void CompleteDanglingFunctionCalls_AnswersCallsWithoutResult_RightAfterExistingResults()
        {
            var history = HistoryWithPendingCall();

            var added = ToolApprovalHistory.CompleteDanglingFunctionCalls(history);

            Assert.Equal(1, added);
            Assert.Equal(4, history.Count);
            var result = Assert.IsType<FunctionResultContent>(Assert.Single(history[3].Items));
            Assert.Equal("call-2", result.CallId);
            Assert.Equal(ToolApprovalHistory.NotRunResult, result.Result);
            Assert.Equal(0, ToolApprovalHistory.CompleteDanglingFunctionCalls(history));
        }

        [Fact]
        public void TryReplaceFunctionResult_ReplacesPlaceholder_RespectingOnlyIfCurrent()
        {
            var history = HistoryWithPendingCall();

            Assert.False(ToolApprovalHistory.TryReplaceFunctionResult(history, "call-1", "x", onlyIfCurrent: "something else"));
            Assert.True(ToolApprovalHistory.TryReplaceFunctionResult(history, "call-1", "deleted", onlyIfCurrent: ToolApprovalHistory.AwaitingApprovalResult));
            Assert.Equal("deleted", ((FunctionResultContent)history[2].Items[0]).Result);
            Assert.False(ToolApprovalHistory.TryReplaceFunctionResult(history, "unknown", "x"));
            Assert.False(ToolApprovalHistory.TryReplaceFunctionResult(history, null, "x"));
        }

        [Theory]
        [InlineData("Deny", ToolApprovalHistory.DeniedResult)]
        [InlineData("Expired", ToolApprovalHistory.ExpiredResult)]
        public void ResultForNotRun_MatchesDecision(string decision, string expected)
        {
            Assert.Equal(expected, ToolApprovalHistory.ResultForNotRun(decision));
        }

        [Theory]
        [InlineData("AllowOnce", "Approved", true)]
        [InlineData("AllowForConversation", "Approved", true)]
        [InlineData("Deny", "Denied", true)]
        [InlineData("Expired", "Expired", false)]
        [InlineData("allowonce", "Expired", false)]
        public void Decision_MapsToStatusAndUserDecision(string decision, string status, bool isUserDecision)
        {
            Assert.Equal(status, ToolApprovalConsts.Decision.ToStatus(decision));
            Assert.Equal(isUserDecision, ToolApprovalConsts.Decision.IsUserDecision(decision));
        }

        [Fact]
        public void GrantKey_IsToolForNonMcp_ToolAndFunctionForMcp()
        {
            Assert.Equal("7:delete_document", Record().GrantKey);
            var http = Record();
            http.IsMcp = false;
            Assert.Equal("7", http.GrantKey);
        }
    }
}
