using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs.Services
{
    public class ChatNotificationService : IChatNotificationService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<ChatNotificationService> _logger;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ChatNotificationService(IHubContext<ChatHub> hubContext, ILogger<ChatNotificationService> logger, IDateTimeProvider dateTimeProvider)
        {
            _hubContext = hubContext;
            _logger = logger;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task SendMessageToChat(string chatId, ChatMessage message, int? agentId = null, string agentName = null, string chatbotIconUrl = null)
        {
            try
            {
                var messageUpdate = new MessageUpdate
                {
                    ChatId = chatId,
                    Message = message,
                    AgentId = agentId,
                    AgentName = agentName,
                    ChatbotIconUrl = chatbotIconUrl
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ReceiveMessage, messageUpdate);
                _logger.LogDebug("Sent message to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to chat {ChatId}", chatId);
            }
        }

        public async Task SendToolExecutionUpdate(string chatId, string toolName, string status, string description = null, int? statusCode = null, string error = null)
        {
            try
            {
                var update = new ToolExecutionUpdate
                {
                    ChatId = chatId,
                    ToolName = toolName,
                    Status = status,
                    Description = description,
                    StatusCode = statusCode,
                    Error = error,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ToolExecutionUpdate, update);
                _logger.LogDebug("Sent tool execution update for {ToolName} in chat {ChatId}", toolName, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send tool execution update to chat {ChatId}", chatId);
            }
        }

        public async Task SendToolExecutionFailedUpdate(string chatId, string toolName, string errorCode, string description = null)
        {
            try
            {
                // US 7959 / Task 7961: a code only; Error (free text) is never filled for a failed tool.
                var update = new ToolExecutionUpdate
                {
                    ChatId = chatId,
                    ToolName = toolName,
                    Status = "failed",
                    Description = description,
                    ErrorCode = errorCode,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ToolExecutionUpdate, update);
                _logger.LogDebug("Sent failed tool execution update for {ToolName} ({ErrorCode}) in chat {ChatId}", toolName, errorCode, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send failed tool execution update to chat {ChatId}", chatId);
            }
        }

        public async Task SendSubagentActivityUpdate(string chatId, int? agentId, string agentName, string status, string chatbotIconUrl = null, string action = null, string orchestrationInstanceId = null, string error = null)
        {
            try
            {
                var update = new SubagentActivityUpdate
                {
                    ChatId = chatId,
                    AgentId = agentId,
                    AgentName = agentName,
                    ChatbotIconUrl = chatbotIconUrl,
                    Action = action,
                    Status = status,
                    OrchestrationInstanceId = orchestrationInstanceId,
                    Error = error,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.SubagentActivityUpdate, update);
                _logger.LogDebug("Sent subagent activity update for agent {AgentId} ({Status}) in chat {ChatId}", agentId, status, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send subagent activity update to chat {ChatId}", chatId);
            }
        }

        public async Task SendSubagentActivityFailedUpdate(string chatId, int? agentId, string agentName, string errorCode, string chatbotIconUrl = null, string action = null, string orchestrationInstanceId = null)
        {
            try
            {
                // US 7959 / Task 7963: a code only; Error (free text) is never filled for a failed sub-agent.
                var update = new SubagentActivityUpdate
                {
                    ChatId = chatId,
                    AgentId = agentId,
                    AgentName = agentName,
                    ChatbotIconUrl = chatbotIconUrl,
                    Action = action,
                    Status = SubagentActivityStatus.Failed,
                    OrchestrationInstanceId = orchestrationInstanceId,
                    ErrorCode = errorCode,
                    Timestamp = _dateTimeProvider.DateTimeOffsetUtcNow()
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.SubagentActivityUpdate, update);
                _logger.LogDebug("Sent failed subagent activity update for agent {AgentId} ({ErrorCode}) in chat {ChatId}", agentId, errorCode, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send failed subagent activity update to chat {ChatId}", chatId);
            }
        }

        public async Task SendTypingIndicator(string chatId, bool isTyping, int? agentId = null, string agentName = null, string chatbotIconUrl = null)
        {
            try
            {
                var update = new TypingIndicatorUpdate
                {
                    ChatId = chatId,
                    IsTyping = isTyping,
                    AgentId = agentId,
                    AgentName = agentName,
                    ChatbotIconUrl = chatbotIconUrl
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.TypingIndicator, update);
                _logger.LogDebug("Sent typing indicator to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send typing indicator to chat {ChatId}", chatId);
            }
        }

        public async Task SendStreamingTokens(string chatId, string messageId, DateTimeOffset messageTimestamp, List<string> tokens, AuthorRole role, ChannelMessageType channelMessageType, bool isComplete = false, int? agentId = null, string agentName = null, string chatbotIconUrl = null)
        {
            try
            {
                var update = new StreamingTokenUpdate
                {
                    ChatId = chatId,
                    MessageId = messageId,
                    Type = role.ToString(),
                    Tokens = tokens.ToArray(),
                    ChannelMessageType = channelMessageType,
                    IsComplete = isComplete,
                    AgentId = agentId,
                    AgentName = agentName,
                    ChatbotIconUrl = chatbotIconUrl,
                    Timestamp = messageTimestamp.ToUniversalTime()
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.StreamingTokens, update);
                _logger.LogDebug("Sent streaming token to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send streaming token to chat {ChatId}", chatId);
            }
        }

        public async Task SendConversationStateUpdate(string chatId, string state, Dictionary<string, object> metadata = null)
        {
            try
            {
                var update = new ConversationStateUpdate
                {
                    ChatId = chatId,
                    State = state,
                    Metadata = metadata
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ConversationStateUpdate, update);
                _logger.LogDebug("Sent conversation state update to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send conversation state update to chat {ChatId}", chatId);
            }
        }

        public async Task SendAudioChunkAsync(string chatId, string base64Audio, string text, bool isFinal)
        {
            try
            {
                var update = new AudioChunkUpdate
                {
                    ChatId = chatId,
                    Base64Audio = base64Audio,
                    Text = text,
                    IsFinal = isFinal
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.StreamingAudio, update);
                _logger.LogDebug("Sent audio chunk to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send audio chunk to chat {ChatId}", chatId);
                throw;
            }
        }

        public async Task SendContactFormRequestAsync(string chatId, ContactFormUpdate update)
        {
            try
            {
                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ContactFormRequest, update);
                _logger.LogDebug("Sent contact form request to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact form request to chat {ChatId}", chatId);
            }
        }

        public async Task SendContactFormCompletedAsync(string chatId, string originalMessage)
        {
            try
            {
                var update = new ContactFormCompletedUpdate
                {
                    ChatId = chatId,
                    OriginalMessage = originalMessage ?? string.Empty
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ContactFormCompleted, update);
                _logger.LogDebug("Sent contact form completed to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact form completed to chat {ChatId}", chatId);
            }
        }

        public async Task SendToolApprovalRequestAsync(string chatId, ToolApprovalRequestUpdate update)
        {
            try
            {
                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ToolApprovalRequest, update);
                _logger.LogDebug("Sent tool approval request {ApprovalId} to chat {ChatId}", update?.ApprovalId, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send tool approval request to chat {ChatId}", chatId);
            }
        }

        public async Task SendToolApprovalResolvedAsync(string chatId, ToolApprovalResolvedUpdate update)
        {
            try
            {
                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ToolApprovalResolved, update);
                _logger.LogDebug("Sent tool approval resolution {ApprovalId} to chat {ChatId}", update?.ApprovalId, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send tool approval resolution to chat {ChatId}", chatId);
            }
        }

        public async Task SendConversationStoppedAsync(string chatId, DateTimeOffset stoppedAt)
        {
            try
            {
                var update = new ConversationStoppedUpdate
                {
                    ChatId = chatId,
                    StoppedAt = stoppedAt
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ConversationStopped, update);
                _logger.LogDebug("Sent conversation stopped to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send conversation stopped to chat {ChatId}", chatId);
            }
        }

        public async Task SendConversationTitleUpdatedAsync(string chatId, string title)
        {
            try
            {
                var update = new ConversationTitleUpdate
                {
                    ChatId = chatId,
                    Title = title
                };

                await _hubContext.Clients.Group(chatId).SendAsync(HubReceiverMethods.ConversationTitleUpdated, update);
                _logger.LogDebug("Sent conversation title to chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send conversation title to chat {ChatId}", chatId);
            }
        }
    }
}
