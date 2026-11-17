using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// US 7959 / Task 7963: the resume turn after a tool approval decision failed. Reported once per approval (keyed on its
    /// approval id, so the handler and the background queue never both report it), with a code only:
    /// <list type="bullet">
    /// <item>the chat's own agent (no root agent): the resume was the turn - a terminal error ends it;</item>
    /// <item>a hand-off sub-agent: only that agent failed (the chat's agent already answered) - its bubble stops, a
    /// non-terminal marker, the chat back to idle, then its sub-agent card turns "failed" with the code.</item>
    /// </list>
    /// Nothing when the user stopped the session after the resume started. Never throws.
    /// </summary>
    public static class ToolApprovalResumeFailure
    {
        public static string DedupeKey(string approvalId) => $"approval:{approvalId}";

        /// <param name="reporter">Null (no chat error reporting available): only a sub-agent's failed card is sent.</param>
        /// <param name="answerDelivered">
        /// The resume turn's answer already reached the chat in full (<c>IChatTurnFailureState.AnswerDelivered</c>). Same rule
        /// as the single-agent turns (Task 7961): a terminal failure after that (e.g. saving the history threw) is only
        /// logged; the agent's bubble stops and the chat leaves "processing".
        /// </param>
        public static async Task ReportAsync(IChatAgentFailureReporter reporter, IChatNotificationService notificationService, ToolApprovalRecord record,
            string errorCode, DateTimeOffset? resumeStartedAt, ILogger logger, bool answerDelivered = false)
        {
            try
            {
                var code = ChatAgentFailure.ToAgentErrorCode(errorCode);
                var subAgent = record.RootAgentId != null;

                if (!subAgent && answerDelivered)
                {
                    logger?.LogWarning("Tool approval {ApprovalId}: the resume turn of agent {AgentId} failed with {ErrorCode} after its answer was delivered; not reported to the chat",
                        record.ApprovalId, record.AgentId, code);
                    await notificationService.SendTypingIndicator(record.SessionId, false, record.AgentId, record.AgentName, record.ChatbotIconUrl);
                    await notificationService.SendConversationStateUpdate(record.SessionId, "idle");
                    return;
                }

                var notified = reporter == null || await reporter.ReportAsync(new ChatAgentFailure
                {
                    TenantName = record.TenantName,
                    SessionId = record.SessionId,
                    TurnId = record.ApprovalId,
                    TurnStartedAt = resumeStartedAt,
                    ErrorCode = code,
                    AgentId = record.AgentId,
                    AgentName = record.AgentName,
                    ChatbotIconUrl = record.ChatbotIconUrl,
                    IsTerminal = !subAgent,
                    EnsureIdle = subAgent,
                    TypingAgents = subAgent ? [] : [new ChatAgentRef { AgentId = record.AgentId, AgentName = record.AgentName }],
                    DedupeKey = DedupeKey(record.ApprovalId)
                });

                if (notified && subAgent)
                    await SendFailedCardAsync(notificationService, record, code);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Reporting the failed resume of tool approval {ApprovalId} to session {SessionId} failed", record?.ApprovalId, record?.SessionId);
            }
        }

        /// <summary>Bug 7722 / Task 7963: the sub-agent's card of a hand-off approval turns "failed", with a code and no text.</summary>
        public static Task SendFailedCardAsync(IChatNotificationService notificationService, ToolApprovalRecord record, string errorCode) =>
            notificationService.SendSubagentActivityFailedUpdate(record.SessionId, record.AgentId, record.AgentName,
                ChatAgentFailure.ToAgentErrorCode(errorCode), record.ChatbotIconUrl, action: record.Chat?.SubagentActivityAction);
    }
}
