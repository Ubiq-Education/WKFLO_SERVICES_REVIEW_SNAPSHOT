using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator
{
    /// <summary>Input of <see cref="NotifyChatErrorWorkflowActivity"/> (serializable).</summary>
    public class NotifyChatErrorRequest
    {
        public ChatAgentFailure Failure { get; set; }

        /// <summary>Also send the agent's "failed" sub-agent card (with its code), once the chat was notified.</summary>
        public bool SendFailedActivity { get; set; }

        public string OrchestrationInstanceId { get; set; }

        /// <summary>The root agent's widget context, for the agent's chat icon (see <c>AgentChatIconHelper</c>).</summary>
        public bool? RootIsWidget { get; set; }
    }

    /// <summary>
    /// US 7959 / Task 7963: the side effect of <see cref="MultiAgentOrchestratorWorkflow"/> reporting a failure to the chat
    /// (workflow code must stay deterministic, so the SignalR sends happen here). Called without a retry policy, after the
    /// agent's own retries are exhausted: one failed card (with its code) and one live marker per agent, deduped on the
    /// orchestration instance id, never after a Stop. Never throws.
    /// </summary>
    public class NotifyChatErrorWorkflowActivity : WorkflowActivity<NotifyChatErrorRequest, bool>
    {
        private readonly IChatAgentFailureReporter _reporter;
        private readonly IChatNotificationService _notificationService;
        private readonly IMasterDataDbContext _db;
        private readonly ILogger<NotifyChatErrorWorkflowActivity> _logger;

        public NotifyChatErrorWorkflowActivity(IChatAgentFailureReporter reporter, IChatNotificationService notificationService, IMasterDataDbContext db,
            ILogger<NotifyChatErrorWorkflowActivity> logger)
        {
            _reporter = reporter;
            _notificationService = notificationService;
            _db = db;
            _logger = logger;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, NotifyChatErrorRequest input)
        {
            var failure = input?.Failure;
            if (failure == null)
                return false;

            try
            {
                if (failure.AgentId is { } agentId && failure.ChatbotIconUrl == null)
                    failure.ChatbotIconUrl = await TryGetChatbotIconUrlAsync(agentId, input.RootIsWidget);

                var notified = await _reporter.ReportAsync(failure, CancellationToken.None);

                if (notified && input.SendFailedActivity && failure.AgentId.HasValue)
                {
                    await _notificationService.SendSubagentActivityFailedUpdate(failure.SessionId, failure.AgentId, failure.AgentName,
                        ChatAgentFailure.ToAgentErrorCode(failure.ErrorCode), failure.ChatbotIconUrl, orchestrationInstanceId: input.OrchestrationInstanceId);
                }

                return notified;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MultiAgentOrchestrator {OrchestrationInstanceId}: reporting failure {ErrorCode} of agent {AgentId} to the chat failed",
                    input.OrchestrationInstanceId, failure.ErrorCode, failure.AgentId);
                return false;
            }
        }

        private async Task<string> TryGetChatbotIconUrlAsync(int agentId, bool? rootIsWidget)
        {
            try
            {
                return await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, agentId, rootIsWidget);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Resolving the chat icon of agent {AgentId} failed", agentId);
                return null;
            }
        }
    }
}
