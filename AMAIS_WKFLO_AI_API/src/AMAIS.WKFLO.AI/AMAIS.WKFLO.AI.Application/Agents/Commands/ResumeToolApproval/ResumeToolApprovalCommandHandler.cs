using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.SignalR;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval
{
    public class ResumeToolApprovalCommandHandler : IRequestHandler<ResumeToolApprovalCommand, Result>
    {
        private readonly IToolApprovalStateService _stateService;
        private readonly IRunnableAISyncService _runnableAISyncService;
        private readonly ChatContext _chatContext;
        private readonly ILogger<ResumeToolApprovalCommandHandler> _logger;
        private readonly IToolApprovalNotifier _notifier;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IChatNotificationService _notificationService;
        private readonly IChatAgentFailureReporter _failureReporter;
        private readonly IChatTurnFailureState _chatTurnFailureState;

        /// <param name="failureReporter">US 7959 / Task 7963: tells the chat the resume turn failed. Null: only a sub-agent's failed card is sent.</param>
        /// <param name="chatTurnFailureState">US 7959 / Task 7963: what the resume turn recorded about its failure (same scope).</param>
        public ResumeToolApprovalCommandHandler(IToolApprovalStateService stateService, IRunnableAISyncService runnableAISyncService, ChatContext chatContext,
            ILogger<ResumeToolApprovalCommandHandler> logger, IToolApprovalNotifier notifier, IDateTimeProvider dateTimeProvider,
            IChatNotificationService notificationService, IChatAgentFailureReporter failureReporter = null, IChatTurnFailureState chatTurnFailureState = null)
        {
            _failureReporter = failureReporter;
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _notificationService = notificationService;
            _logger = logger;
            _notifier = notifier;
            _dateTimeProvider = dateTimeProvider;
            _stateService = stateService;
            _runnableAISyncService = runnableAISyncService;
            _chatContext = chatContext;
        }

        public async ValueTask<Result> Handle(ResumeToolApprovalCommand command, CancellationToken cancellationToken)
        {
            var record = await _stateService.GetAsync(command.ApprovalId, cancellationToken);

            // Only a decided (approved or denied) approval of this same session is ever resumed; expired ones are not.
            if (record == null || record.SessionId != command.SessionId
                || (record.Status != ToolApprovalConsts.Status.Approved && record.Status != ToolApprovalConsts.Status.Denied))
            {
                _logger.LogWarning("Tool approval {ApprovalId} not resumed: record {Found}, status {Status}, session match {SessionMatch}",
                    command.ApprovalId, record != null, record?.Status, record?.SessionId == command.SessionId);
                return new NotFoundResult();
            }

            // An approved call must not run when the agent was switched off/deleted or its share revoked since the
            // request: the decision becomes a refusal (card + chat updated), nothing is executed, no turn runs.
            if (record.Status == ToolApprovalConsts.Status.Approved && !await _runnableAISyncService.CanResumeAsync(record, cancellationToken))
            {
                _logger.LogWarning("Tool approval {ApprovalId} not run: agent {AgentId} is no longer active/available (share {ShareId})",
                    record.ApprovalId, record.AgentId, record.ShareId);
                var refused = await _stateService.MarkRefusedAsync(record.ApprovalId, _dateTimeProvider.DateTimeOffsetUtcNow(), cancellationToken);
                if (refused != null)
                {
                    await _notifier.NotifyResolvedAsync(refused);
                    await _notifier.PersistNotRunAsync(refused, ToolApprovalHistory.NoLongerAvailableResult);
                }
                // US 7959 / Task 7963: a code on the sub-agent's card, no text (the card itself already shows the refusal).
                if (record.RootAgentId != null)
                    await TrySendAsync(() => ToolApprovalResumeFailure.SendFailedCardAsync(_notificationService, record, ChatErrorCodes.ChatAgentUnavailable));
                return new NotFoundResult();
            }

            // Bug 7722: sub-agent activity of a hand-off approval. Allow: resumed -> completed; Deny: denied once the
            // resumed turn (which continues without the call) has finished. Either way the last status is failed when
            // the resumed turn fails, or the awaiting_approval sent by the approval filter when it stops on another approval.
            var approved = record.Status == ToolApprovalConsts.Status.Approved;
            if (approved)
                await SendSubagentActivityAsync(record, SubagentActivityStatus.Resumed);

            // The person who decided is chatting live: a further Ask in the resumed turn can be approved too.
            _chatContext.ToolApprovalsAvailable = true;
            _chatContext.SessionId = record.SessionId;

            // US 7959 / Task 7963: a Stop recorded after this instant is not reported as an error.
            var resumeStartedAt = _dateTimeProvider.DateTimeOffsetUtcNow();

            string answer;
            try
            {
                answer = await _runnableAISyncService.ResumeAfterToolApprovalAsync(record, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A code only; the exception is logged by the queue that runs this resume.
                var code = RecordedErrorCode() ?? ChatErrorClassifier.Classify(ex, stopRequested: false) ?? ChatErrorCodes.ChatTurnFailed;
                await ToolApprovalResumeFailure.ReportAsync(_failureReporter, _notificationService, record, code, resumeStartedAt, _logger,
                    _chatTurnFailureState.AnswerDelivered);
                throw;
            }

            // The run failed (recorded), or produced nothing: no agent to run any more (switched off / deleted since) or a
            // failure before anything was recorded.
            if (answer == null || RecordedErrorCode() != null)
            {
                var code = RecordedErrorCode() ?? ChatErrorCodes.ChatAgentUnavailable;
                _logger.LogWarning("Tool approval {ApprovalId}: the resume turn of agent {AgentId} failed with {ErrorCode}", record.ApprovalId, record.AgentId, code);
                await ToolApprovalResumeFailure.ReportAsync(_failureReporter, _notificationService, record, code, resumeStartedAt, _logger,
                    _chatTurnFailureState.AnswerDelivered);
                return new NotFoundResult();
            }

            // Stopped on another approval: its "awaiting_approval" was sent when it was requested (before its card) and
            // its resume/expiry sends the next status - nothing more here.
            if (!_chatContext.ToolApprovalRequestedThisTurn)
                await SendSubagentActivityAsync(record, approved ? SubagentActivityStatus.Completed : SubagentActivityStatus.Denied);

            return new SuccessResult<string>(answer);
        }

        private string RecordedErrorCode() =>
            _chatTurnFailureState.Failure is { Severity: ChatErrorSeverity.Error } failure ? failure.Code : null;

        private async Task TrySendAsync(Func<Task> send)
        {
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sending a sub-agent activity update failed");
            }
        }

        // Bug 7722: only approvals of a hand-off sub-agent (requested on behalf of a root agent's chat) have an activity.
        private Task SendSubagentActivityAsync(ToolApprovalRecord record, string status) =>
            record.RootAgentId == null
                ? Task.CompletedTask
                : ToolApprovalNotifier.SendSubagentActivityAsync(_notificationService, record, status, logger: _logger);
    }
}
