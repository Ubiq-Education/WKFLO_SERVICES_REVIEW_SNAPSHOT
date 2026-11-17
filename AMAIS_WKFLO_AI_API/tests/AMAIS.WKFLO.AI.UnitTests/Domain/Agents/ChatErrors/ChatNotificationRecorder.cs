using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959 / Task 7961: a mock <see cref="IChatNotificationService"/> that keeps every SignalR payload in order, so tests
    /// can check what the chat received (and that no exception text ever reached it).
    /// </summary>
    public sealed class ChatNotificationRecorder
    {
        public Mock<IChatNotificationService> Mock { get; } = new();

        public List<(string Method, object Payload)> Sent { get; } = [];

        public ChatNotificationRecorder()
        {
            Mock.Setup(x => x.SendTypingIndicator(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, bool isTyping, int? agentId, string agentName, string icon) =>
                    Add(HubReceiverMethods.TypingIndicator, new TypingIndicatorUpdate { ChatId = chatId, IsTyping = isTyping, AgentId = agentId, AgentName = agentName, ChatbotIconUrl = icon }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendMessageToChat(It.IsAny<string>(), It.IsAny<ChatMessage>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, ChatMessage message, int? agentId, string agentName, string icon) =>
                    Add(HubReceiverMethods.ReceiveMessage, new MessageUpdate { ChatId = chatId, Message = message, AgentId = agentId, AgentName = agentName, ChatbotIconUrl = icon }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendConversationStateUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                .Callback((string chatId, string state, Dictionary<string, object> metadata) =>
                    Add(HubReceiverMethods.ConversationStateUpdate, new ConversationStateUpdate { ChatId = chatId, State = state, Metadata = metadata }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendToolExecutionUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()))
                .Callback((string chatId, string toolName, string status, string description, int? statusCode, string error) =>
                    Add(HubReceiverMethods.ToolExecutionUpdate, new ToolExecutionUpdate { ChatId = chatId, ToolName = toolName, Status = status, Description = description, StatusCode = statusCode, Error = error }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendToolExecutionFailedUpdate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, string toolName, string errorCode, string description) =>
                    Add(HubReceiverMethods.ToolExecutionUpdate, new ToolExecutionUpdate { ChatId = chatId, ToolName = toolName, Status = "failed", Description = description, ErrorCode = errorCode }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendSubagentActivityUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, int? agentId, string agentName, string status, string icon, string action, string orchestrationInstanceId, string error) =>
                    Add(HubReceiverMethods.SubagentActivityUpdate, new SubagentActivityUpdate { ChatId = chatId, AgentId = agentId, AgentName = agentName, Status = status, ChatbotIconUrl = icon, Action = action, OrchestrationInstanceId = orchestrationInstanceId, Error = error }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendSubagentActivityFailedUpdate(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, int? agentId, string agentName, string errorCode, string icon, string action, string orchestrationInstanceId) =>
                    Add(HubReceiverMethods.SubagentActivityUpdate, new SubagentActivityUpdate { ChatId = chatId, AgentId = agentId, AgentName = agentName, Status = SubagentActivityStatus.Failed, ChatbotIconUrl = icon, Action = action, OrchestrationInstanceId = orchestrationInstanceId, ErrorCode = errorCode }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendStreamingTokens(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<List<string>>(), It.IsAny<AuthorRole>(), It.IsAny<ChannelMessageType>(),
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string chatId, string messageId, DateTimeOffset timestamp, List<string> tokens, AuthorRole role, ChannelMessageType type, bool isComplete, int? agentId, string agentName, string icon) =>
                    Add("ReceiveStreamingTokens", new { chatId, messageId, tokens, isComplete, agentId, agentName }))
                .Returns(Task.CompletedTask);
            Mock.Setup(x => x.SendAudioChunkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback((string chatId, string audio, string text, bool isFinal) => Add("ReceiveAudioChunk", new { chatId, text, isFinal }))
                .Returns(Task.CompletedTask);
        }

        public IChatNotificationService Object => Mock.Object;

        private void Add(string method, object payload)
        {
            lock (Sent)
                Sent.Add((method, payload));
        }

        /// <summary>Every payload as the JSON the hub would send.</summary>
        public string AllPayloadsJson() => string.Join("\n", Sent.Select(s => s.Method + " " + JsonSerializer.Serialize(s.Payload)));

        /// <summary>The live failed-turn markers sent, in order.</summary>
        public List<MessageUpdate> Markers() => Sent
            .Where(s => s.Method == HubReceiverMethods.ReceiveMessage)
            .Select(s => (MessageUpdate)s.Payload)
            .Where(m => m.Message?.Metadata != null && m.Message.Metadata.ContainsKey(CommonMetadataConsts.ErrorCode))
            .ToList();

        /// <summary>The typing indicators sent for <paramref name="agentId"/>, in order.</summary>
        public List<bool> TypingOf(int? agentId) => Sent
            .Where(s => s.Method == HubReceiverMethods.TypingIndicator)
            .Select(s => (TypingIndicatorUpdate)s.Payload)
            .Where(t => t.AgentId == agentId)
            .Select(t => t.IsTyping)
            .ToList();

        /// <summary>The sub-agent activity updates sent for <paramref name="agentId"/>, in order.</summary>
        public List<SubagentActivityUpdate> SubagentUpdatesOf(int? agentId) => Sent
            .Where(s => s.Method == HubReceiverMethods.SubagentActivityUpdate)
            .Select(s => (SubagentActivityUpdate)s.Payload)
            .Where(u => u.AgentId == agentId)
            .ToList();

        /// <summary>The conversation states sent, in order.</summary>
        public List<string> States() => Sent
            .Where(s => s.Method == HubReceiverMethods.ConversationStateUpdate)
            .Select(s => ((ConversationStateUpdate)s.Payload).State)
            .ToList();

        public void AssertNoLeak(params string[] secrets)
        {
            var json = AllPayloadsJson();
            foreach (var secret in secrets)
                Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
