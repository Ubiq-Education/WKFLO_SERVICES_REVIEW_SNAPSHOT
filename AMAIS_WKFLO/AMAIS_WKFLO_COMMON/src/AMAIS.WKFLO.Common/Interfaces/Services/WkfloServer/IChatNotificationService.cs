using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer
{
    public interface IChatNotificationService
    {
        Task SendMessageToChat(string chatId, ChatMessage message, int? agentId = null, string agentName = null, string chatbotIconUrl = null);
        Task SendToolExecutionUpdate(string chatId, string toolName, string status, string description = null, int? statusCode = null, string error = null);

        /// <summary>
        /// US 7959 / Task 7961: a "failed" <see cref="ToolExecutionUpdate"/> carrying only a chat runtime code
        /// (<see cref="ToolExecutionUpdate.ErrorCode"/>), never free text: <see cref="ToolExecutionUpdate.Error"/> stays empty.
        /// </summary>
        Task SendToolExecutionFailedUpdate(string chatId, string toolName, string errorCode, string description = null);
        Task SendSubagentActivityUpdate(string chatId, int? agentId, string agentName, string status, string chatbotIconUrl = null, string action = null, string orchestrationInstanceId = null, string error = null);

        /// <summary>
        /// US 7959 / Task 7963: a "failed" <see cref="SubagentActivityUpdate"/> carrying only a chat runtime code
        /// (<see cref="SubagentActivityUpdate.ErrorCode"/>), never free text: <see cref="SubagentActivityUpdate.Error"/> stays empty.
        /// </summary>
        Task SendSubagentActivityFailedUpdate(string chatId, int? agentId, string agentName, string errorCode, string chatbotIconUrl = null, string action = null, string orchestrationInstanceId = null);
        Task SendTypingIndicator(string chatId, bool isTyping, int? agentId = null, string agentName = null, string chatbotIconUrl = null);

        /// <param name="messageTimestamp">
        /// US 7974: UTC instant of the message the tokens belong to, taken once when it starts: pass the same value for
        /// every update of <paramref name="messageId"/> and persist it as that message's timestamp.
        /// </param>
        Task SendStreamingTokens(string chatId, string messageId, DateTimeOffset messageTimestamp, List<string> tokens, AuthorRole role, ChannelMessageType messageType, bool isComplete = false, int? agentId = null, string agentName = null, string chatbotIconUrl = null);
        Task SendConversationStateUpdate(string chatId, string state, Dictionary<string, object> metadata = null);
        Task SendAudioChunkAsync(string chatId, string base64Audio, string text, bool isFinal);
        Task SendContactFormRequestAsync(string chatId, ContactFormUpdate update);
        Task SendContactFormCompletedAsync(string chatId, string originalMessage);
        Task SendToolApprovalRequestAsync(string chatId, ToolApprovalRequestUpdate update);
        Task SendToolApprovalResolvedAsync(string chatId, ToolApprovalResolvedUpdate update);

        /// <summary>US 7501: the user stopped every agent of this chat; the UI returns to idle.</summary>
        Task SendConversationStoppedAsync(string chatId, DateTimeOffset stoppedAt);

        /// <summary>US 7507: the conversation got its AI-generated title.</summary>
        Task SendConversationTitleUpdatedAsync(string chatId, string title);
    }
}
