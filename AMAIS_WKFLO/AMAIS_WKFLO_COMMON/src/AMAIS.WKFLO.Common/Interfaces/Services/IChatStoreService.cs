using AMAIS.WKFLO.Common.Models.Results.Chat;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    public interface IChatStoreService
    {
        Task<(int thumbsUp, int thumbsDown)> SaveChatAsync(string tenantName, int agentId, string sessionId, string name, ChatHistory chatHistory, DateTimeOffset createdAt, string userId);
        Task<(DateTimeOffset, string, ChatHistory, Dictionary<string, string>)> LoadChatAsync(string tenantName, int agentId, string sessionId, string userId);
        Task<bool> DeleteChatAsync(string tenantName, int agentId, string sessionId, string userId);
        Task<List<ChatListResult>> ListChatsAsync(string tenantName, int agentId, string userId);
        Task<(bool found, int thumbsUp, int thumbsDown)> RateMessageAsync(string tenantName, int agentId, string sessionId, string userId, string messageId, bool isThumbsUp);
    }
}
