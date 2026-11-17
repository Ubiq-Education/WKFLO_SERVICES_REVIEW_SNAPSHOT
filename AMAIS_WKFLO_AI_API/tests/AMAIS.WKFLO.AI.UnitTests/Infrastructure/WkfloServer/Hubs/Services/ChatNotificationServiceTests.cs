using AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs.Services;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.WkfloServer.Hubs.Services
{
    public class ChatNotificationServiceTests
    {
        private const string ChatId = "session-abc";
        private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 15, TimeSpan.Zero);

        private readonly Mock<IClientProxy> _clientProxyMock;
        private readonly Mock<IHubClients> _hubClientsMock;
        private readonly Mock<IHubContext<ChatHub>> _hubContextMock;
        private readonly ChatNotificationService _service;

        public ChatNotificationServiceTests()
        {
            _clientProxyMock = new Mock<IClientProxy>();
            _hubClientsMock = new Mock<IHubClients>();
            _hubClientsMock.Setup(x => x.Group(ChatId)).Returns(_clientProxyMock.Object);

            _hubContextMock = new Mock<IHubContext<ChatHub>>();
            _hubContextMock.Setup(x => x.Clients).Returns(_hubClientsMock.Object);

            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(Now);

            _service = new ChatNotificationService(_hubContextMock.Object, NullLogger<ChatNotificationService>.Instance, dateTimeProviderMock.Object);
        }

        // US 7974: live events carry a server UTC timestamp.

        [Fact]
        public async Task SendStreamingTokens_CarriesTheMessageTimestampInUtc()
        {
            var messageTimestamp = new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.FromHours(2));

            await _service.SendStreamingTokens(ChatId, "msg-1", messageTimestamp, ["Hi"], AuthorRole.Assistant, ChannelMessageType.Normal);

            var update = SentUpdate<StreamingTokenUpdate>(CommonConsts.HubReceiverMethods.StreamingTokens);
            Assert.Equal(messageTimestamp, update.Timestamp);
            Assert.Equal(TimeSpan.Zero, update.Timestamp.Offset);
        }

        [Fact]
        public async Task SendStreamingTokens_SameMessage_EveryUpdateCarriesTheSameTimestamp()
        {
            var messageTimestamp = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
            var updates = new List<StreamingTokenUpdate>();
            _clientProxyMock
                .Setup(x => x.SendCoreAsync(CommonConsts.HubReceiverMethods.StreamingTokens, It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback((string _, object[] args, CancellationToken _) => updates.Add((StreamingTokenUpdate)args[0]))
                .Returns(Task.CompletedTask);

            await _service.SendStreamingTokens(ChatId, "msg-1", messageTimestamp, ["Hel"], AuthorRole.Assistant, ChannelMessageType.Normal);
            await _service.SendStreamingTokens(ChatId, "msg-1", messageTimestamp, ["lo"], AuthorRole.Assistant, ChannelMessageType.Normal, isComplete: true);

            Assert.Equal(2, updates.Count);
            Assert.All(updates, u => Assert.Equal(messageTimestamp, u.Timestamp));
        }

        [Fact]
        public async Task SendToolExecutionUpdate_CarriesTheServerTimestamp()
        {
            await _service.SendToolExecutionUpdate(ChatId, "Knowledge", "started");

            Assert.Equal(Now, SentUpdate<ToolExecutionUpdate>(CommonConsts.HubReceiverMethods.ToolExecutionUpdate).Timestamp);
        }

        [Fact]
        public async Task SendToolExecutionFailedUpdate_CarriesTheServerTimestamp()
        {
            await _service.SendToolExecutionFailedUpdate(ChatId, "Knowledge", "WKFLOAI1101");

            Assert.Equal(Now, SentUpdate<ToolExecutionUpdate>(CommonConsts.HubReceiverMethods.ToolExecutionUpdate).Timestamp);
        }

        [Fact]
        public async Task SendSubagentActivityUpdate_CarriesTheServerTimestamp()
        {
            await _service.SendSubagentActivityUpdate(ChatId, 5, "Refunds Bot", "started");

            Assert.Equal(Now, SentUpdate<SubagentActivityUpdate>(CommonConsts.HubReceiverMethods.SubagentActivityUpdate).Timestamp);
        }

        [Fact]
        public async Task SendSubagentActivityFailedUpdate_CarriesTheServerTimestamp()
        {
            await _service.SendSubagentActivityFailedUpdate(ChatId, 5, "Refunds Bot", "WKFLOAI1101");

            Assert.Equal(Now, SentUpdate<SubagentActivityUpdate>(CommonConsts.HubReceiverMethods.SubagentActivityUpdate).Timestamp);
        }

        [Fact]
        public void StreamingTokenUpdate_Timestamp_SerializesAsIso8601WithOffset()
        {
            var update = new StreamingTokenUpdate { Timestamp = new DateTimeOffset(2026, 10, 22, 9, 30, 15, TimeSpan.Zero) };

            var json = JsonSerializer.Serialize(update, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Contains("\"timestamp\":\"2026-10-22T09:30:15+00:00\"", json);
        }

        private T SentUpdate<T>(string method)
        {
            var invocation = Assert.Single(_clientProxyMock.Invocations, i => (string)i.Arguments[0] == method);
            return Assert.IsType<T>(((object[])invocation.Arguments[1])[0]);
        }

        [Fact]
        public async Task SendSubagentActivityUpdate_StartedStatus_SendsUpdateToChatGroup()
        {
            await _service.SendSubagentActivityUpdate(
                ChatId, agentId: 5, agentName: "Refunds Bot", status: "started",
                chatbotIconUrl: "https://cdn.test/icon.png", action: "refund-requested", orchestrationInstanceId: "orch-1");

            _clientProxyMock.Verify(x => x.SendCoreAsync(
                CommonConsts.HubReceiverMethods.SubagentActivityUpdate,
                It.Is<object[]>(args => args.Length == 1 && IsExpectedUpdate((SubagentActivityUpdate)args[0],
                    ChatId, 5, "Refunds Bot", "https://cdn.test/icon.png", "refund-requested", "started", "orch-1", null)),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SendSubagentActivityUpdate_FailedStatus_IncludesErrorOnUpdate()
        {
            await _service.SendSubagentActivityUpdate(
                ChatId, agentId: 5, agentName: "Refunds Bot", status: "failed",
                chatbotIconUrl: "https://cdn.test/icon.png", action: "refund-requested", error: "Agent not found.");

            _clientProxyMock.Verify(x => x.SendCoreAsync(
                CommonConsts.HubReceiverMethods.SubagentActivityUpdate,
                It.Is<object[]>(args => args.Length == 1 && ((SubagentActivityUpdate)args[0]).Status == "failed"
                    && ((SubagentActivityUpdate)args[0]).Error == "Agent not found."),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SendSubagentActivityUpdate_HubThrows_SwallowsExceptionWithoutPropagating()
        {
            _clientProxyMock
                .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("hub unavailable"));

            var exception = await Record.ExceptionAsync(() =>
                _service.SendSubagentActivityUpdate(ChatId, 5, "Refunds Bot", "started"));

            Assert.Null(exception);
        }

        private static bool IsExpectedUpdate(SubagentActivityUpdate update, string chatId, int? agentId, string agentName,
            string chatbotIconUrl, string action, string status, string orchestrationInstanceId, string error) =>
            update.ChatId == chatId &&
            update.AgentId == agentId &&
            update.AgentName == agentName &&
            update.ChatbotIconUrl == chatbotIconUrl &&
            update.Action == action &&
            update.Status == status &&
            update.OrchestrationInstanceId == orchestrationInstanceId &&
            update.Error == error;
    }
}
