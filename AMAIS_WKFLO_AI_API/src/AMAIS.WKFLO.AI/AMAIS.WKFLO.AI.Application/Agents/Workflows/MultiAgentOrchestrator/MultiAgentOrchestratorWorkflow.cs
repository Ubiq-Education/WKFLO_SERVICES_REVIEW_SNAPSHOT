using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator
{
    /// <summary>
    /// Orchestrates a multi-agent pipeline: one sequential "lead" agent step, followed by a
    /// fan-out/fan-in step across two or more agents invoked concurrently. Each agent role is invoked
    /// exactly once via <see cref="InvokeAgentWorkflowActivity"/> — the workflow never calls
    /// <see cref="Invocation.IAgentInvoker"/> directly (required for Dapr Workflow replay
    /// determinism). "State persisting across steps" is satisfied at two levels: the workflow's own
    /// execution history (Dapr Workflow replay state) plus the explicit threading of one step's output
    /// into the next step's input below. As of #6553, agent invocation no longer goes through a Dapr
    /// Actor — see <see cref="InvokeAgentWorkflowActivity"/>'s doc comment for why that hop was removed.
    ///
    /// US 7959 / Task 7963: an agent whose run failed for good (a soft failure, or a thrown one after the retries) is
    /// reported to the chat once, as soon as it is known: its failed card with a code, its thinking bubble stopped and a
    /// non-terminal marker (<see cref="NotifyChatErrorWorkflowActivity"/>). When no agent produced an answer and nobody
    /// stopped the run, the run ends with one terminal error. A stopped run reports nothing. The sends happen in that
    /// activity only; what is sent is decided from the activity results, so the workflow stays deterministic.
    /// </summary>
    public class MultiAgentOrchestratorWorkflow : Workflow<MultiAgentOrchestratorWorkflowInput, MultiAgentOrchestratorWorkflowOutput>
    {
        /// <summary>
        /// Retry policy applied to every <see cref="InvokeAgentWorkflowActivity"/> call (lead and
        /// fan-out). Covers transient infrastructure failures on the agent-invocation path (e.g. Claude
        /// API timeouts/rate limits surfacing as transport errors) — NOT the business-level soft
        /// failures already modelled by <see cref="AgentInvocationResult.Success"/>, which
        /// <see cref="Invocation.IAgentInvoker"/> catches internally and never throws for. Three
        /// attempts with exponential backoff starting at 2s (2s, 4s, 8s, capped at 30s) gives transient
        /// failures a realistic chance to clear without holding up the pipeline for long; the 2-minute
        /// overall retry timeout bounds total wall-clock time spent retrying a single step.
        /// US 7501: a step stopped by the person is returned as <see cref="AgentInvocationResult.Cancelled"/>, never
        /// thrown, so this policy does not retry it.
        /// </summary>
        private static readonly WorkflowRetryPolicy ActivityRetryPolicy = new(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(2),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromSeconds(30),
            retryTimeout: TimeSpan.FromMinutes(2));

        private static readonly WorkflowTaskOptions ActivityRetryOptions = new(RetryPolicy: ActivityRetryPolicy);

        public override async Task<MultiAgentOrchestratorWorkflowOutput> RunAsync(WorkflowContext context, MultiAgentOrchestratorWorkflowInput input)
        {
            var orchestrationInstanceId = context.InstanceId;

            // Workflow code replays on every history rebuild, so a plain injected ILogger would log
            // duplicate lines on each replay. CreateReplaySafeLogger only emits when
            // context.IsReplaying is false, per the Dapr.Workflow SDK's own guidance.
            var logger = context.CreateReplaySafeLogger<MultiAgentOrchestratorWorkflow>();

            // US 7501: replay-safe start time of this orchestration, so a Stop also cancels steps that start after it.
            var requestedAt = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero);

            // --- Sequential step: one lead agent, invoked once. Wrapped the same way as the fan-out
            // calls below so that a lead-agent call which still fails after exhausting
            // ActivityRetryPolicy degrades to a soft failure (fan-out proceeds with the original
            // message, see below) instead of throwing and failing the entire orchestration. ---
            var leadResult = await InvokeAndReportFailureAsync(
                context,
                logger,
                input,
                new AgentInvocationRequest
                {
                    AgentId = input.LeadAgent.AgentId,
                    AgentRole = input.LeadAgent.AgentRole,
                    Input = input.Input,
                    SessionId = input.SessionId,
                    OrchestrationInstanceId = orchestrationInstanceId,
                    ChainOfThought = input.ChainOfThought,
                    TenantContext = input.TenantContext,
                    ChatContext = input.ChatContext,
                    UserContext = input.UserContext,
                    AgentSharableContext = input.AgentSharableContext,
                    RootIsWidget = input.RootIsWidget,
                    RequestedAt = requestedAt
                });

            // US 7501: the person stopped the session during the lead step; the fan-out agents must not start.
            if (leadResult.Cancelled)
            {
                return new MultiAgentOrchestratorWorkflowOutput
                {
                    OrchestrationInstanceId = orchestrationInstanceId,
                    LeadResult = leadResult,
                    FanOutResults = [],
                    Success = false
                };
            }

            // --- Fan-out step: every reviewer agent gets the original message plus the lead agent's
            // answer, and runs concurrently via its own activity invocation. The original file
            // attachments (if any) are forwarded unchanged to every fan-out agent too, not just the
            // lead. ---
            var fanOutText = leadResult.Success
                ? $"Original request: {input.Input.Text}\n\nLead agent ({input.LeadAgent.AgentRole}) response: {leadResult.Content}"
                : input.Input.Text;

            var fanOutInput = new AgentInvocationInput { Text = fanOutText, Files = input.Input.Files };

            var fanOutTasks = input.FanOutAgents
                .Select(agent => InvokeAndReportFailureAsync(
                    context,
                    logger,
                    input,
                    new AgentInvocationRequest
                    {
                        AgentId = agent.AgentId,
                        AgentRole = agent.AgentRole,
                        Input = fanOutInput,
                        SessionId = input.SessionId,
                        OrchestrationInstanceId = orchestrationInstanceId,
                        ChainOfThought = input.ChainOfThought,
                        TenantContext = input.TenantContext,
                        ChatContext = input.ChatContext,
                        UserContext = input.UserContext,
                        AgentSharableContext = input.AgentSharableContext,
                        RootIsWidget = input.RootIsWidget,
                        RequestedAt = requestedAt
                    }))
                .ToList();

            // --- Fan-in: wait for every concurrent agent call, then aggregate. Every task above
            // already catches its own terminal failure, so Task.WhenAll never faults here — one
            // participant's ultimate (post-retry) failure never cancels its siblings. ---
            var fanOutResults = await Task.WhenAll(fanOutTasks);

            // US 7959 / Task 7963: nobody answered and nobody stopped the run - it ends with one turn-level error.
            await ReportRunFailureAsync(context, logger, input, requestedAt, [leadResult, .. fanOutResults]);

            return new MultiAgentOrchestratorWorkflowOutput
            {
                OrchestrationInstanceId = orchestrationInstanceId,
                LeadResult = leadResult,
                FanOutResults = fanOutResults.ToList(),
                Success = leadResult.Success && fanOutResults.All(r => r.Success)
            };
        }

        /// <summary>
        /// Calls <see cref="InvokeAgentWorkflowActivity"/> with <see cref="ActivityRetryPolicy"/>
        /// applied, and isolates that single agent's ultimate (post-retry) failure so it can never
        /// fault the surrounding <see cref="Task.WhenAll(Task[])"/> fan-in or abort the workflow.
        /// Dapr already retries transient <see cref="WorkflowTaskFailedException"/> failures per
        /// <see cref="ActivityRetryPolicy"/> before this method ever sees them; this catch only runs
        /// once retries are exhausted, converting the terminal failure into the same
        /// <see cref="AgentInvocationResult.Success"/> == <c>false</c> soft-failure shape that
        /// <see cref="Invocation.IAgentInvoker"/> uses for business-level failures, so the caller's
        /// aggregation logic (<c>leadResult.Success &amp;&amp; fanOutResults.All(r =&gt; r.Success)</c>)
        /// handles both cases identically.
        /// </summary>
        private static async Task<AgentInvocationResult> InvokeAgentIsolatedAsync(
            WorkflowContext context,
            ILogger logger,
            AgentInvocationRequest request)
        {
            try
            {
                return await context.CallActivityAsync<AgentInvocationResult>(
                    nameof(InvokeAgentWorkflowActivity),
                    request,
                    ActivityRetryOptions);
            }
            catch (WorkflowTaskFailedException ex)
            {
                if (!context.IsReplaying)
                {
                    logger.LogError(ex,
                        "MultiAgentOrchestrator {OrchestrationInstanceId}: agent {AgentRole} (TenantAgent {AgentId}) " +
                        "failed permanently after exhausting the retry policy ({MaxNumberOfAttempts} attempts)",
                        request.OrchestrationInstanceId, request.AgentRole, request.AgentId,
                        ActivityRetryPolicy.MaxNumberOfAttempts);
                }

                // US 7959 / Task 7963: a code only (the classifier is pure, so this stays deterministic); the exception
                // was logged above.
                var errorCode = ChatErrorClassifier.Classify(ex, stopRequested: false) ?? ChatErrorCodes.ChatTurnFailed;
                return new AgentInvocationResult
                {
                    AgentRole = request.AgentRole,
                    AgentId = request.AgentId,
                    Success = false,
                    ErrorCode = errorCode,
                    ErrorMessage = $"Agent invocation failed after retries ({errorCode}).",
                    CompletedAtUtc = new DateTimeOffset(context.CurrentUtcDateTime, TimeSpan.Zero)
                };
            }
        }

        /// <summary>
        /// US 7959 / Task 7963: <see cref="InvokeAgentIsolatedAsync"/>, then - when the agent failed for good and was not
        /// stopped - its failure is reported to the chat right away (once per agent of this orchestration).
        /// </summary>
        private static async Task<AgentInvocationResult> InvokeAndReportFailureAsync(
            WorkflowContext context,
            ILogger logger,
            MultiAgentOrchestratorWorkflowInput input,
            AgentInvocationRequest request)
        {
            var result = await InvokeAgentIsolatedAsync(context, logger, request);

            if (!result.Success && !result.Cancelled)
            {
                await NotifyChatErrorAsync(context, logger, new NotifyChatErrorRequest
                {
                    Failure = new ChatAgentFailure
                    {
                        TenantName = input.TenantContext?.TenantName,
                        SessionId = input.SessionId,
                        TurnId = context.InstanceId,
                        TurnStartedAt = request.RequestedAt,
                        ErrorCode = result.ErrorCode ?? ChatErrorCodes.ChatTurnFailed,
                        AgentId = request.AgentId,
                        AgentName = request.AgentRole,
                        IsTerminal = false,
                        DedupeKey = $"{context.InstanceId}:agent:{request.AgentId}"
                    },
                    SendFailedActivity = true,
                    OrchestrationInstanceId = context.InstanceId,
                    RootIsWidget = input.RootIsWidget
                });
            }

            return result;
        }

        /// <summary>
        /// US 7959 / Task 7963: one terminal error when no agent produced an answer and none was stopped. Its code is the
        /// agents' common code when they all failed the same way (e.g. every call was rate limited), else
        /// <see cref="ChatErrorCodes.ChatSubagentFailed"/>.
        /// </summary>
        private static Task ReportRunFailureAsync(
            WorkflowContext context,
            ILogger logger,
            MultiAgentOrchestratorWorkflowInput input,
            DateTimeOffset requestedAt,
            IReadOnlyList<AgentInvocationResult> results)
        {
            if (results.Any(r => r.Success || r.Cancelled))
                return Task.CompletedTask;

            var codes = results.Select(r => r.ErrorCode ?? ChatErrorCodes.ChatTurnFailed).Distinct().ToList();
            var errorCode = codes.Count == 1 ? codes[0] : ChatErrorCodes.ChatSubagentFailed;

            return NotifyChatErrorAsync(context, logger, new NotifyChatErrorRequest
            {
                Failure = new ChatAgentFailure
                {
                    TenantName = input.TenantContext?.TenantName,
                    SessionId = input.SessionId,
                    TurnId = context.InstanceId,
                    TurnStartedAt = requestedAt,
                    ErrorCode = errorCode,
                    IsTerminal = true,
                    TypingAgents = results.Select(r => new ChatAgentRef { AgentId = r.AgentId, AgentName = r.AgentRole }).ToList()
                },
                OrchestrationInstanceId = context.InstanceId,
                RootIsWidget = input.RootIsWidget
            });
        }

        /// <summary>
        /// Calls <see cref="NotifyChatErrorWorkflowActivity"/> without a retry policy (it never throws, and a duplicate
        /// would be deduped anyway). Nothing for a run without a chat. A failure to notify never fails the orchestration.
        /// </summary>
        private static async Task NotifyChatErrorAsync(WorkflowContext context, ILogger logger, NotifyChatErrorRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Failure?.SessionId))
                return;

            try
            {
                await context.CallActivityAsync<bool>(nameof(NotifyChatErrorWorkflowActivity), request);
            }
            catch (WorkflowTaskFailedException ex)
            {
                if (!context.IsReplaying)
                {
                    logger.LogWarning(ex, "MultiAgentOrchestrator {OrchestrationInstanceId}: reporting failure {ErrorCode} of agent {AgentId} to the chat failed",
                        context.InstanceId, request.Failure.ErrorCode, request.Failure.AgentId);
                }
            }
        }
    }
}
