using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval
{
    /// <summary>
    /// WI 7575: tells the chat a decision/expiry happened (<c>ToolApprovalResolved</c>) and, for outcomes that
    /// have no resume turn (expiry), writes the card's final state into the persisted chat history.
    /// </summary>
    public interface IToolApprovalNotifier
    {
        Task NotifyResolvedAsync(ToolApprovalRecord record);

        /// <summary>Updates the persisted card (and the call's placeholder result) of an expired approval.</summary>
        Task PersistExpiredAsync(ToolApprovalRecord record);

        /// <summary>Updates the persisted card and replaces the call's placeholder result with <paramref name="toolResult"/>.</summary>
        Task PersistNotRunAsync(ToolApprovalRecord record, string toolResult);
    }

    public class ToolApprovalNotifier : IToolApprovalNotifier
    {
        private readonly IChatNotificationService _notificationService;
        private readonly IChatStoreService _chatStoreService;
        private readonly ILogger<ToolApprovalNotifier> _logger;

        public ToolApprovalNotifier(IChatNotificationService notificationService, IChatStoreService chatStoreService, ILogger<ToolApprovalNotifier> logger)
        {
            _notificationService = notificationService;
            _chatStoreService = chatStoreService;
            _logger = logger;
        }

        public async Task NotifyResolvedAsync(ToolApprovalRecord record)
        {
            await _notificationService.SendToolApprovalResolvedAsync(record.SessionId, new ToolApprovalResolvedUpdate
            {
                ChatId = record.SessionId,
                ApprovalId = record.ApprovalId,
                Decision = record.Decision
            });

            // Bug 7722: an expired approval of a hand-off sub-agent has no resume turn; its activity ends here.
            if (record.RootAgentId != null && record.Decision == ToolApprovalConsts.Decision.Expired)
                await SendSubagentActivityAsync(_notificationService, record, SubagentActivityStatus.Expired, logger: _logger);
        }

        /// <summary>
        /// Bug 7722: a sub-agent activity update for the sub-agent that requested <paramref name="record"/> (same fields
        /// as the hand-off's started/completed updates). Never throws: a notification failure must not break the flow.
        /// </summary>
        public static async Task SendSubagentActivityAsync(IChatNotificationService notificationService, ToolApprovalRecord record,
            string status, string error = null, ILogger logger = null)
        {
            try
            {
                await notificationService.SendSubagentActivityUpdate(record.SessionId, record.AgentId, record.AgentName, status,
                    record.ChatbotIconUrl, action: record.Chat?.SubagentActivityAction, error: error);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Sending the {Status} sub-agent activity update of tool approval {ApprovalId} failed", status, record.ApprovalId);
            }
        }

        public Task PersistExpiredAsync(ToolApprovalRecord record) => PersistNotRunAsync(record, ToolApprovalHistory.ExpiredResult);

        public async Task PersistNotRunAsync(ToolApprovalRecord record, string toolResult)
        {
            try
            {
                // Same (tenant, agent, session, user) as the turn that saved the card - see the chat store's path rules.
                var (createdAt, name, chatHistory, _) = await _chatStoreService.LoadChatAsync(record.TenantName, record.AgentId, record.SessionId, record.UserId);
                if (chatHistory == null || !ToolApprovalHistory.TryUpdateCard(chatHistory, record))
                    return;

                ToolApprovalHistory.TryReplaceFunctionResult(chatHistory, record.CallId, toolResult,
                    onlyIfCurrent: ToolApprovalHistory.AwaitingApprovalResult);

                await _chatStoreService.SaveChatAsync(record.TenantName, record.AgentId, record.SessionId, name, chatHistory, createdAt, record.UserId);
            }
            catch (Exception ex)
            {
                // Not fatal: the next turn of the conversation brings the card up to date as well.
                _logger.LogWarning(ex, "Persisting the outcome of tool approval {ApprovalId} failed", record.ApprovalId);
            }
        }
    }
}
