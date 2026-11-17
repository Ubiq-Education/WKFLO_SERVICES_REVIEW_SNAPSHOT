using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.SignalR;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator
{
    /// <summary>
    /// Invokes one agent role/step of a multi-agent orchestration. Dapr Workflow replay requires that
    /// external/side-effecting calls happen inside an activity, never directly in workflow code — this
    /// activity is reused for both the sequential lead step and every concurrent fan-out call. As of
    /// #6553 this calls <see cref="IAgentInvoker"/> directly (a plain scoped service) rather than a
    /// Dapr Actor: the former <c>AgentActor</c> was a fresh, one-shot RPC per call with no
    /// reminders/timers and no state surviving past the call, so it never needed the Actor runtime —
    /// only its non-remoting JSON invocation, which an in-process call gets for free with no
    /// serialization boundary at all.
    ///
    /// US 7501: the run is registered as a chat turn of its session, so a Stop cancels it. A stopped run is
    /// returned as <see cref="AgentInvocationResult.Cancelled"/> instead of thrown, so the workflow's retry policy
    /// never runs it again.
    ///
    /// US 7959 / Task 7963: a failed run never sends the "failed" update itself: a thrown failure may still be retried by
    /// the workflow's retry policy, and the workflow reports each agent's final failure once, after the retries
    /// (<see cref="NotifyChatErrorWorkflowActivity"/>), with a code and never exception text.
    /// </summary>
    public class InvokeAgentWorkflowActivity : WorkflowActivity<AgentInvocationRequest, AgentInvocationResult>
    {
        private static readonly ActivitySource ActivitySource = new("AMAIS.WKFLO.AI.MultiAgentOrchestrator", "1.0.0");

        private readonly IAgentInvoker _agentInvoker;
        private readonly IMasterDataDbContext _db;
        private readonly IChatNotificationService _notificationService;
        private readonly ILogger<InvokeAgentWorkflowActivity> _logger;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly IChatStopCheckpoint _stopCheckpoint;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatTurnErrorReporter _chatTurnErrorReporter;

        /// <param name="chatTurnErrorReporter">US 7959 / Task 7963: lets the run report its tool warnings live. Null: they are only recorded.</param>
        public InvokeAgentWorkflowActivity(IAgentInvoker agentInvoker, IMasterDataDbContext db,
            IChatNotificationService notificationService, ILogger<InvokeAgentWorkflowActivity> logger,
            IChatTurnRegistry turnRegistry, IChatStopCheckpoint stopCheckpoint, IDateTimeProvider dateTimeProvider,
            IChatTurnErrorReporter chatTurnErrorReporter = null)
        {
            _chatTurnErrorReporter = chatTurnErrorReporter;
            _turnRegistry = turnRegistry;
            _stopCheckpoint = stopCheckpoint;
            _dateTimeProvider = dateTimeProvider;
            _agentInvoker = agentInvoker;
            _db = db;
            _notificationService = notificationService;
            _logger = logger;
        }

        public override async Task<AgentInvocationResult> RunAsync(WorkflowActivityContext context, AgentInvocationRequest input)
        {
            using var activity = ActivitySource.StartActivity("InvokeAgent", ActivityKind.Internal);

            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("wkflo.agent_role", input.AgentRole);
                activity.SetTag("wkflo.runnable_agent_id", input.AgentId);
                activity.SetTag("wkflo.orchestration_instance_id", input.OrchestrationInstanceId);
            }

            _logger.LogInformation(
                "MultiAgentOrchestrator: invoking agent for TenantAgent {AgentId} (role {AgentRole}), task execution {TaskExecutionKey}",
                input.AgentId, input.AgentRole, context.TaskExecutionKey);

            // task #7297: push a live SignalR update so the chat UI can render this subagent's activity
            // in the timeline as it happens, rather than only showing the eventual aggregated result.
            var chatbotIconUrl = await TrySendActivityUpdateAsync(input, "started", error: null);

            IChatTurn turn = null;
            try
            {
                var cancellationToken = CancellationToken.None;
                if (!string.IsNullOrWhiteSpace(input.SessionId))
                {
                    // A Stop only reaches turns of its own tenant: the run is registered under the invocation's tenant.
                    var tenantName = input.TenantContext?.TenantName;
                    turn = _turnRegistry.Register(tenantName, input.SessionId, input.RequestedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow());
                    cancellationToken = turn.Token;

                    // The hand-offs this run requests belong to it (see ChatContext.TurnStartedAt).
                    if (input.ChatContext != null)
                        input.ChatContext.TurnStartedAt = turn.StartedAt;

                    // Single read of the stop state: covers a Stop this replica's subscription missed.
                    await _stopCheckpoint.ThrowIfStoppedAsync(tenantName, input.SessionId, ChatStopCheckpoints.AgentInvocationStart, cancellationToken);

                    // US 7959 / Task 7963: the run's tool warnings reach the chat as they happen, once per tool per orchestration.
                    var runTurn = turn;
                    _chatTurnErrorReporter?.BeginTurn(new ChatTurnErrorTurn
                    {
                        TenantName = tenantName,
                        SessionId = input.SessionId,
                        TurnId = input.OrchestrationInstanceId ?? context.InstanceId,
                        TurnStartedAt = runTurn.StartedAt,
                        IsStopRequested = () => runTurn.StopRequested
                    });
                }

                var result = await _agentInvoker.InvokeAsync(input, cancellationToken);

                activity?.SetTag("wkflo.agent_invocation_success", result.Success);
                activity?.SetStatus(result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

                // US 7959 / Task 7963: a failure is reported once by the workflow (see the class remarks), not here.
                if (result.Success)
                    await TrySendActivityUpdateAsync(input, SubagentActivityStatus.Completed, error: null, chatbotIconUrl);
                else
                    _logger.LogWarning("MultiAgentOrchestrator: TenantAgent {AgentId} (role {AgentRole}) failed with {ErrorCode}: {ErrorMessage}",
                        input.AgentId, input.AgentRole, result.ErrorCode, result.ErrorMessage);

                return result;
            }
            catch (OperationCanceledException) when (turn?.StopRequested == true)
            {
                // US 7501 (approved change 4): stopped by the person. Returned, not thrown, so the workflow's retry
                // policy does not run this agent again.
                _logger.LogInformation(
                    "MultiAgentOrchestrator: TenantAgent {AgentId} (role {AgentRole}) was stopped by the user, task execution {TaskExecutionKey}",
                    input.AgentId, input.AgentRole, context.TaskExecutionKey);

                activity?.SetTag("wkflo.agent_invocation_cancelled", true);

                await TrySendActivityUpdateAsync(input, SubagentActivityStatus.Cancelled, error: null, chatbotIconUrl);

                return new AgentInvocationResult
                {
                    AgentRole = input.AgentRole,
                    AgentId = input.AgentId,
                    Success = false,
                    Cancelled = true,
                    CompletedAtUtc = _dateTimeProvider.DateTimeOffsetUtcNow()
                };
            }
            catch (Exception ex)
            {
                // AgentInvoker.InvokeAsync already catches its own business/model-call failures (e.g.
                // Claude API errors) and returns them as a soft failure (AgentInvocationResult.Success
                // == false) rather than throwing. Anything reaching this catch is a genuinely unexpected
                // failure outside that handling — log it, tag the span, and rethrow so Dapr Workflow's
                // retry policy (see MultiAgentOrchestratorWorkflow.ActivityRetryPolicy) decides whether
                // to retry; this activity method simply runs again from the top on the next attempt.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName,
                    ["exception.message"] = ex.Message
                }));

                _logger.LogError(ex,
                    "MultiAgentOrchestrator: agent invocation failed for TenantAgent {AgentId} (role {AgentRole}), " +
                    "task execution {TaskExecutionKey} — Dapr's retry policy will decide whether to retry",
                    input.AgentId, input.AgentRole, context.TaskExecutionKey);

                // US 7959 / Task 7963: no "failed" update here - this attempt may be retried; the workflow reports the final failure.
                throw;
            }
            finally
            {
                turn?.Dispose();
            }
        }

        // Resolves the subagent's chat icon (once, reused across the started/completed/failed updates for
        // this same invocation — see AgentChatIconHelper's guidance) and pushes the SignalR update.
        // Notification failures must never break agent invocation itself, so they're logged and swallowed.
        private async Task<string> TrySendActivityUpdateAsync(AgentInvocationRequest input, string status, string error, string chatbotIconUrl = null)
        {
            try
            {
                chatbotIconUrl ??= await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, input.AgentId, input.RootIsWidget);

                await _notificationService.SendSubagentActivityUpdate(
                    input.SessionId, input.AgentId, input.AgentRole, status, chatbotIconUrl,
                    orchestrationInstanceId: input.OrchestrationInstanceId, error: error);

                return chatbotIconUrl;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MultiAgentOrchestrator: failed to send subagent activity update ({Status}) for TenantAgent {AgentId}",
                    status, input.AgentId);

                return chatbotIconUrl;
            }
        }
    }
}
