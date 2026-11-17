using AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AMAIS.WKFLO.AI.Application.Agents.Invocation
{
    /// <summary>
    /// See <see cref="IAgentInvoker"/> — replaces the former Dapr Actor <c>AgentActor</c>. Registered
    /// Scoped: the caller (a Workflow Activity or an ASP.NET Core pub/sub controller) already provides
    /// a fresh DI scope per invocation, so <see cref="TenantContext"/>/<see cref="ChatContext"/>/
    /// <see cref="UserContext"/>/<see cref="AgentSharableContext"/> can be restored directly onto the
    /// constructor-injected instances — no manual <c>IServiceProvider.CreateScope()</c> needed, unlike
    /// the actor version (a Dapr Actor's own DI lifetime isn't per-call).
    /// </summary>
    public class AgentInvoker : IAgentInvoker
    {
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IWkfloMapper _mapper;
        private readonly IWkfloMediator _mediator;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<AgentInvoker> _logger;
        private readonly IChatTurnFailureState _chatTurnFailureState;

        public AgentInvoker(
            TenantContext tenantContext,
            ChatContext chatContext,
            UserContext userContext,
            AgentSharableContext agentSharableContext,
            IWkfloMapper mapper,
            IWkfloMediator mediator,
            IDateTimeProvider dateTimeProvider,
            ILogger<AgentInvoker> logger,
            IChatTurnFailureState chatTurnFailureState = null)
        {
            // US 7959 / Task 7963: what the run recorded about its failure (same scope as the run).
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Tag whichever span is already ambient (the caller's own activity span — either
            // InvokeAgentWorkflowActivity's ActivitySource span on the workflow path, or ASP.NET
            // Core's auto-instrumented request span on the AgentEventSubscriber pub/sub path).
            // No new span is started here: this call is now in-process (no second Dapr Actor hop),
            // so there is only one logical span to enrich, not two.
            var activity = Activity.Current;

            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("wkflo.agent_role", request.AgentRole);
                activity.SetTag("wkflo.runnable_agent_id", request.AgentId);
                activity.SetTag("wkflo.orchestration_instance_id", request.OrchestrationInstanceId);
            }

            _logger.LogInformation(
                "AgentInvoker invoking TenantAgent {AgentId} (role {AgentRole}) for orchestration {OrchestrationInstanceId}",
                request.AgentId, request.AgentRole, request.OrchestrationInstanceId);

            AgentInvocationResult result;

            try
            {
                _mapper.Map(request.TenantContext, _tenantContext);
                _mapper.Map(request.ChatContext, _chatContext);
                _mapper.Map(request.UserContext, _userContext);
                _mapper.Map(request.AgentSharableContext, _agentSharableContext);

                var commandResult = await _mediator.Send(new InvokeAgentCommand
                {
                    Id = request.AgentId,
                    AgentRole = request.AgentRole,
                    Input = request.Input,
                    SessionId = request.SessionId,
                    ChainOfThought = request.ChainOfThought,
                    RootIsWidget = request.RootIsWidget,
                    ApprovalRootAgentId = request.ApprovalRootAgentId
                }, cancellationToken);

                result = commandResult switch
                {
                    SuccessResult<AgentInvocationResult> success => success.Data,
                    _ => new AgentInvocationResult
                    {
                        AgentRole = request.AgentRole,
                        AgentId = request.AgentId,
                        Success = false,
                        ErrorCode = ErrorCodeOf(commandResult),
                        ErrorMessage = DescribeFailure(commandResult),
                        CompletedAtUtc = _dateTimeProvider.DateTimeOffsetUtcNow()
                    }
                };

                // US 7501: a run stopped by the person is not a failure; its caller records it as cancelled. A run that
                // completed successfully just before the Stop is reported as completed, never turned into a cancel.
                if (!result.Success)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                // US 7501: stopped by the person. Rethrown (not turned into a soft failure) so the caller can tell a
                // Stop apart from an error. Some providers wrap the cancellation, hence the unwrapped rethrow.
                if (ex is OperationCanceledException)
                    throw;

                throw new OperationCanceledException("The agent invocation was stopped.", ex, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AgentInvoker failed invoking TenantAgent {AgentId} (role {AgentRole})",
                    request.AgentId, request.AgentRole);

                // US 7959 / Task 7963: the chat only ever gets a code; the exception stays in the log above.
                var errorCode = RecordedErrorCode() ?? ChatErrorClassifier.Classify(ex, stopRequested: false) ?? ChatErrorCodes.ChatTurnFailed;
                result = new AgentInvocationResult
                {
                    AgentRole = request.AgentRole,
                    AgentId = request.AgentId,
                    Success = false,
                    ErrorCode = errorCode,
                    ErrorMessage = $"Agent invocation failed ({errorCode}).",
                    CompletedAtUtc = _dateTimeProvider.DateTimeOffsetUtcNow()
                };
            }

            if (!string.IsNullOrEmpty(request.OrchestrationInstanceId))
            {
                result.Metadata[nameof(request.OrchestrationInstanceId)] = request.OrchestrationInstanceId;
            }

            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("wkflo.agent_invocation_success", result.Success);
                activity.SetStatus(result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            }

            return result;
        }

        /// <summary>
        /// US 7959 / Task 7963: the chat runtime code of a command result that is not a success: what the run recorded first;
        /// otherwise a validation failure or a missing / not runnable agent is <see cref="ChatErrorCodes.ChatAgentUnavailable"/>,
        /// anything else <see cref="ChatErrorCodes.ChatTurnFailed"/>.
        /// </summary>
        private string ErrorCodeOf(Result result) =>
            RecordedErrorCode() ?? result switch
            {
                InvalidResult or NotFoundResult => ChatErrorCodes.ChatAgentUnavailable,
                _ => ChatErrorCodes.ChatTurnFailed
            };

        private string RecordedErrorCode() =>
            _chatTurnFailureState.Failure is { Severity: ChatErrorSeverity.Error } failure ? failure.Code : null;

        private static string DescribeFailure(Result result)
        {
            if (result is ErrorResult<Error> errorResult && errorResult.Errors.Count > 0)
            {
                return string.Join("; ", errorResult.Errors.Select(e => $"{e.Code}:{e.Property}"));
            }

            return result switch
            {
                NotFoundResult => "Agent not found.",
                null => "Agent invocation returned no result.",
                _ => $"Agent invocation failed with result type {result.ResultType}."
            };
        }
    }
}
