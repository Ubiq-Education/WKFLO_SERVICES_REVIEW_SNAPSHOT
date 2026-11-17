using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr.Client;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.AI.Application.Common.Activities
{
    public abstract class BaseWorkflowActivity<T, K> : WorkflowActivity<T, K> where T : class
    {
        /// <summary>US 7501: idempotency status of a turn stopped by the user. Terminal: never retried or resumed.</summary>
        internal const string CancelledStatus = "cancelled";

        protected readonly IWkfloMediator _mediator;
        protected readonly ILogger _logger;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        protected readonly DaprClient _daprClient;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatTurnRegistry _turnRegistry;
        private readonly IChatStopCheckpoint _stopCheckpoint;
        private readonly IChatNotificationService _notificationService;
        private readonly IChatTurnErrorNotifier _chatTurnErrorNotifier;
        private readonly IChatTurnFailureState _chatTurnFailureState;
        private readonly IChatTurnErrorReporter _chatTurnErrorReporter;

        /// <param name="chatTurnErrorNotifier">US 7959 / Task 7961: reports a failed turn to the chat. Null: failures are only logged.</param>
        /// <param name="chatTurnFailureState">US 7959 / Task 7961: what the lower layers recorded about this turn's failure.</param>
        /// <param name="chatTurnErrorReporter">US 7959 / Task 7961: lets the lower layers report tool warnings of this turn live.</param>
        public BaseWorkflowActivity(ILogger logger, IWkfloMediator mediator, IWkfloMapper mapper, IDateTimeProvider dateTimeProvider,
            TenantContext tenantContext, ChatContext chatContext, UserContext userContext, AgentSharableContext agentSharableContext, DaprClient daprClient,
            IChatTurnRegistry turnRegistry, IChatStopCheckpoint stopCheckpoint, IChatNotificationService notificationService,
            IChatTurnErrorNotifier chatTurnErrorNotifier = null, IChatTurnFailureState chatTurnFailureState = null, IChatTurnErrorReporter chatTurnErrorReporter = null)
        {
            _chatTurnErrorNotifier = chatTurnErrorNotifier;
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _chatTurnErrorReporter = chatTurnErrorReporter;
            _turnRegistry = turnRegistry;
            _stopCheckpoint = stopCheckpoint;
            _notificationService = notificationService;
            _logger = logger;
            _mediator = mediator;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
            _daprClient = daprClient;
            _dateTimeProvider = dateTimeProvider;
        }

        protected void LoadContext<V>(WkfloQueueMessage<V> message) where V : IWkfloQueueMessageCommand
        {
            _mapper.Map(message.ChatContext, _chatContext);
            _mapper.Map(message.TenantContext, _tenantContext);
            _mapper.Map(message.UserContext, _userContext);
            _mapper.Map(message.AgentSharableContext, _agentSharableContext);
        }

        /// <param name="onStoppedBeforeRun">
        /// US 7502: called when the user stopped the session before this queued turn reached its run (its checkpoint found
        /// the Stop), with the message's contexts loaded. Lets the turn still save the "Stopped" marker.
        /// Failures are logged, never thrown. A turn stopped once running saves itself.
        /// </param>
        protected async Task<bool> BaseRunAsync<V>(WorkflowActivityContext context, string messageKey, Func<V, Task> onStoppedBeforeRun = null)
            where V : IWkfloQueueMessageCommand
        {
            string idempotencyKey = null;
            WkfloQueueMessage<V> message = null;
            var reachedRun = false;
            bool returnState = false;
            bool cancelled = false;
            string sessionId = null;
            string tenantName = null;
            IChatTurn turn = null;

            // US 7959 / Task 7961: why this turn failed, when it did - reported to the chat once, at the end.
            ChatTurnFailure failure = null;
            try
            {
                message = await _daprClient.GetStateAsync<WkfloQueueMessage<V>>(CommonConsts.CommonAiPubSubConsts.StateStoreName, messageKey);

                if (message == null)
                {
                    // If message is null, it means the state was deleted on the finally block if returnState = true meaning it was already processed,
                    // so we can skip processing.
                    return true;
                }

                // Generate idempotency key
                idempotencyKey = $"v2-{typeof(V).Name}-{context.InstanceId}-{message.Command.GetId()}";

                // Check if already processed
                var state = await _daprClient.GetStateAsync<IdempotencyState>(CommonConsts.CommonAiPubSubConsts.StateStoreName, idempotencyKey);
                if (state != null)
                {
                    if (state.Status == "done")
                    {
                        _logger.LogInformation("Already completed {idempotencyKey}", idempotencyKey);
                        return true;
                    }

                    // US 7501: a stopped turn is finished; it must never be resumed (not even as stale processing).
                    if (state.Status == CancelledStatus)
                    {
                        _logger.LogInformation("Cancelled by the user {idempotencyKey}, skipping", idempotencyKey);
                        return true;
                    }

                    if (state.Status == "failed")
                    {
                        _logger.LogWarning("{CommandType} Activity failed for instance {InstanceId}, idempotent key: {idempotencyKey}, message payload: {payload}",
                            message.Command.GetType().ToString(),
                            context.InstanceId,
                            idempotencyKey,
                            JsonConvert.SerializeObject(message.Command, LogJsonSettings.Default));
                        return false;
                    }

                    if (state.Status == "processing")
                    {
                        // 🔥 critical: detect stale processing
                        if (state.UpdatedAt > _dateTimeProvider.DateTimeOffsetUtcNow().AddMinutes(-5))
                        {
                            _logger.LogWarning("Still processing {idempotencyKey}, skipping", idempotencyKey);
                            return true;
                        }

                        _logger.LogWarning("Stale processing detected, retrying {idempotencyKey}", idempotencyKey);
                    }
                }

                _logger.LogInformation("{CommandType} Activity started {InstanceId}", typeof(V).Name, context.InstanceId);

                // US 7501: register the turn so a Stop (broadcast to every replica) can cancel it. A message queued
                // before the Stop counts as started before it, even when it is only picked up now.
                sessionId = message.Command.GetId();
                var cancellationToken = CancellationToken.None;
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    // A Stop only reaches turns of its own tenant: the turn is registered under the message tenant.
                    tenantName = message.TenantContext?.TenantName;
                    turn = _turnRegistry.Register(tenantName, sessionId, message.QueuedAt ?? _dateTimeProvider.DateTimeOffsetUtcNow());
                    cancellationToken = turn.Token;

                    // The hand-offs this turn requests belong to it (see ChatContext.TurnStartedAt); restored by LoadContext.
                    if (message.ChatContext != null)
                        message.ChatContext.TurnStartedAt = turn.StartedAt;

                    // Single read of the stop state: covers a Stop this replica's subscription missed.
                    await _stopCheckpoint.ThrowIfStoppedAsync(tenantName, sessionId, ChatStopCheckpoints.TurnStart, cancellationToken);
                }

                // Mark as processing
                await _daprClient.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName,
                    idempotencyKey,
                    new IdempotencyState
                    {
                        Status = "processing",
                        UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
                    });

                // Run business logic
                LoadContext(message);
                BeginErrorReporting(tenantName, sessionId, context.InstanceId, turn);
                reachedRun = true;
                var result = await _mediator.Send(message.Command, cancellationToken);

                // US 7959 / Task 7961: a result that is not a success (worker-side validation, agent not found), or a
                // failure the run recorded while still returning (e.g. the model stream failed), fails the turn. The
                // turn is still finished ("done"): nothing would retry it.
                failure = FailureOf(result);

                // Mark as completed
                await _daprClient.SaveStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName,
                    idempotencyKey,
                    new IdempotencyState
                    {
                        Status = "done",
                        UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
                    });



                returnState = true;
            }
            catch (OperationCanceledException) when (turn?.StopRequested == true)
            {
                // US 7501: stopped by the user - neither a failure nor a timeout. Terminal, so it is never retried.
                _logger.LogInformation("{CommandType} Activity cancelled by the user for instance {InstanceId}: session {SessionId} (tenant {TenantName}), status set to cancelled for {IdempotencyKey}",
                    typeof(V).Name, context.InstanceId, sessionId, tenantName, idempotencyKey);
                cancelled = true;

                if (idempotencyKey != null)
                {
                    await _daprClient.SaveStateAsync(
                        CommonConsts.CommonAiPubSubConsts.StateStoreName,
                        idempotencyKey,
                        new IdempotencyState
                        {
                            Status = CancelledStatus,
                            UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
                        });
                }

                // US 7502: stopped while still queued - the run, which saves a stopped turn itself, never started.
                if (!reachedRun && onStoppedBeforeRun != null && message != null)
                {
                    try
                    {
                        LoadContext(message);
                        await onStoppedBeforeRun(message.Command);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{CommandType} saving the turn stopped before its run failed for instance {InstanceId}", typeof(V).Name, context.InstanceId);
                    }
                }

                returnState = true;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "{CommandType} Activity Timeout for instance {InstanceId}", typeof(V).Name, context.InstanceId);
                failure ??= FailureOf(ex, turn);

                if (idempotencyKey != null)
                {
                    // Mark as failed
                    await _daprClient.SaveStateAsync(
                        CommonConsts.CommonAiPubSubConsts.StateStoreName,
                        idempotencyKey,
                        new IdempotencyState
                        {
                            Status = "failed",
                            UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
                        });
                }
                returnState = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{CommandType} Activity failed for instance {InstanceId}", typeof(V).Name, context.InstanceId);
                failure ??= FailureOf(ex, turn);

                if (idempotencyKey != null)
                {
                    // Mark as failed
                    await _daprClient.SaveStateAsync(
                        CommonConsts.CommonAiPubSubConsts.StateStoreName,
                        idempotencyKey,
                        new IdempotencyState
                        {
                            Status = "failed",
                            UpdatedAt = _dateTimeProvider.DateTimeOffsetUtcNow()
                        });
                }

                returnState = false; // or throw if you want Dapr to retry
            }
            finally
            {
                turn?.Dispose();

                if (cancelled)
                {
                    await _daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, messageKey);

                    // The Stop endpoint already sent these; sent again in case the turn's own events arrived after them.
                    // Not when a newer turn of the session already runs (Stop then Send): the chat now shows that run,
                    // and a late typing-off/idle would hide it.
                    if (turn == null || !_turnRegistry.HasRunningTurnStartedAfter(tenantName, sessionId, turn.StartedAt))
                    {
                        await _notificationService.SendTypingIndicator(sessionId, false);
                        await _notificationService.SendConversationStateUpdate(sessionId, "idle");
                    }
                    else
                    {
                        _logger.LogInformation("{CommandType} Activity cancelled for instance {InstanceId}: a newer turn of session {SessionId} is running, typing-off and idle not sent again",
                            typeof(V).Name, context.InstanceId, sessionId);
                    }
                }
                else if (returnState)
                {
                    _logger.LogInformation("{CommandType} Activity completed {InstanceId}", typeof(V).Name, context.InstanceId);
                    await _daprClient.DeleteStateAsync(CommonConsts.CommonAiPubSubConsts.StateStoreName, messageKey);
                }
                else
                {
                    _logger.LogWarning("{CommandType} Activity failed for instance {InstanceId}", typeof(V).Name, context.InstanceId);
                }

                // US 7959 / Task 7961: the single place a single-agent text/voice turn's failure reaches the chat. Never
                // for a Stop (cancelled is not failed). Never throws.
                if (!cancelled)
                    await ReportFailureAsync(failure, tenantName, sessionId, context.InstanceId, turn, typeof(V).Name);
            }

            return returnState;
        }

        /// <summary>US 7959 / Task 7961: lets the lower layers report this turn's tool warnings to the chat as they happen.</summary>
        private void BeginErrorReporting(string tenantName, string sessionId, string turnId, IChatTurn turn)
        {
            if (_chatTurnErrorReporter == null || turn == null)
                return;

            _chatTurnErrorReporter.BeginTurn(new ChatTurnErrorTurn
            {
                TenantName = tenantName,
                SessionId = sessionId,
                TurnId = turnId,
                TurnStartedAt = turn.StartedAt,
                IsStopRequested = () => turn.StopRequested
            });
        }

        /// <summary>
        /// US 7959 / Task 7961: the failure behind a returned <paramref name="result"/>, or null when the turn succeeded.
        /// What the run recorded comes first; otherwise a validation failure is <see cref="ChatErrorCodes.ChatAgentUnavailable"/>
        /// with its already-public code as the detail code (allowlist only), a missing agent the same code without one, and
        /// anything else <see cref="ChatErrorCodes.ChatTurnFailed"/>.
        /// </summary>
        private ChatTurnFailure FailureOf(Result result)
        {
            var recorded = RecordedError();
            if (recorded != null || result?.ResultType is ResultType.Ok or ResultType.Created or ResultType.NoContent)
                return recorded;

            return result switch
            {
                InvalidResult invalid => ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable,
                    detailCode: ChatErrorCodes.ToPublicDetailCode(invalid.Errors.Select(e => e.Code))),
                NotFoundResult => ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable),
                _ => ChatTurnFailure.Create(ChatErrorCodes.ChatTurnFailed)
            };
        }

        /// <summary>
        /// US 7959 / Task 7961: the failure behind an exception that ended the run: what the run recorded first, else the
        /// classified exception. Null for a Stop.
        /// </summary>
        private ChatTurnFailure FailureOf(Exception exception, IChatTurn turn)
        {
            var stopRequested = turn?.StopRequested == true;
            if (stopRequested)
                return null;

            if (RecordedError() is { } recorded)
                return recorded;

            var code = ChatErrorClassifier.Classify(exception, stopRequested);
            return code == null ? null : ChatTurnFailure.Create(code);
        }

        /// <summary>
        /// The error the run recorded, if any. Warnings are left out: they were already reported live and never fail the turn.
        /// </summary>
        private ChatTurnFailure RecordedError() =>
            _chatTurnFailureState.Failure is { Severity: ChatErrorSeverity.Error } failure ? failure : null;

        private async Task ReportFailureAsync(ChatTurnFailure failure, string tenantName, string sessionId, string turnId, IChatTurn turn, string commandType)
        {
            if (failure == null || _chatTurnErrorNotifier == null || string.IsNullOrWhiteSpace(sessionId) || turn?.StopRequested == true)
                return;

            try
            {
                // The failing agent's bubble must stop: when the layer that failed did not know the agent, it is the agent
                // that was answering.
                var agent = _chatTurnErrorReporter?.CurrentAgent;
                if (!failure.AgentId.HasValue && agent?.AgentId != null)
                    failure = failure with { AgentId = agent.AgentId, AgentName = agent.AgentName, ChatbotIconUrl = agent.ChatbotIconUrl };

                // The user already has the whole answer: a later failure (e.g. saving the history) is not shown as a failed
                // turn. The chat still leaves "processing", unless a newer turn of the session already runs.
                if (failure.IsTerminal && _chatTurnFailureState.AnswerDelivered)
                {
                    _logger.LogWarning("{CommandType} turn {TurnId} of session {SessionId} failed with {ErrorCode} after its answer was delivered; not reported to the chat",
                        commandType, turnId, sessionId, failure.Code);

                    if (turn == null || !_turnRegistry.HasRunningTurnStartedAfter(tenantName, sessionId, turn.StartedAt))
                    {
                        await _notificationService.SendTypingIndicator(sessionId, false, failure.AgentId, failure.AgentName, failure.ChatbotIconUrl);
                        await _notificationService.SendConversationStateUpdate(sessionId, "idle");
                    }

                    return;
                }

                await _chatTurnErrorNotifier.NotifyAsync(new ChatTurnErrorNotification
                {
                    TenantName = tenantName,
                    SessionId = sessionId,
                    TurnId = turnId,
                    TurnStartedAt = turn?.StartedAt,
                    StopRequested = turn?.StopRequested == true,
                    Failure = failure
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{CommandType} reporting the failure of turn {TurnId} to session {SessionId} failed", commandType, turnId, sessionId);
            }
        }
    }
}

