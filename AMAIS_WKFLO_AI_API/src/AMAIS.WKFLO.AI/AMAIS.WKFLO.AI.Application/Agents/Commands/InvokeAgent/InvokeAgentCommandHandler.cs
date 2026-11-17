using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent
{
    /// <summary>
    /// Delegates to <see cref="IRunnableAISyncService"/> (covers both <c>TenantAgent</c> and
    /// <c>PublishedTenantAgent</c> — see <c>RunnableAISyncService</c>) so agent loading, provider
    /// resolution and the Semantic Kernel invocation are reused rather than duplicated here.
    /// Conversation history is untouched — <c>IRunnableAISyncService</c> already loads/saves it via
    /// the blob-based <c>IChatStoreService</c> path keyed by <see cref="InvokeAgentCommand.SessionId"/>.
    ///
    /// US 7959 / Task 7963: a run that failed (recorded in the scope's <see cref="IChatTurnFailureState"/>, e.g. the model
    /// call failed before anything streamed) or answered with nothing is returned as a failed
    /// <see cref="AgentInvocationResult"/> carrying its <see cref="AgentInvocationResult.ErrorCode"/> - never as a success,
    /// and never as "agent not found".
    /// </summary>
    public class InvokeAgentCommandHandler : IRequestHandler<InvokeAgentCommand, Result>
    {
        private readonly IRunnableAISyncService _runnableAISyncService;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ChatContext _chatContext;
        private readonly IChatTurnFailureState _chatTurnFailureState;

        public InvokeAgentCommandHandler(IRunnableAISyncService runnableAISyncService, IDateTimeProvider dateTimeProvider, ChatContext chatContext,
            IChatTurnFailureState chatTurnFailureState = null)
        {
            _runnableAISyncService = runnableAISyncService;
            _dateTimeProvider = dateTimeProvider;
            _chatContext = chatContext;
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
        }

        public async ValueTask<Result> Handle(InvokeAgentCommand command, CancellationToken cancellationToken)
        {
            var runnableAgentData = await _runnableAISyncService.RunRunnableAgentByIdAsync(
                command.Id, command.Input.Text, command.Input.Files, command.SessionId, command.ChainOfThought, command.RootIsWidget,
                command.ApprovalRootAgentId, cancellationToken);

            // What the run recorded comes first: a run that failed is not "agent not found", nor a success. Tool warnings
            // were already reported live and do not fail the run.
            if (_chatTurnFailureState.Failure is { Severity: ChatErrorSeverity.Error } failure)
                return Failed(command, failure.Code);

            if (runnableAgentData == null)
            {
                return new NotFoundResult();
            }

            // Bug 7722: set by the approval filter of this run (same scope) when it stopped on an Ask.
            var awaitingApproval = _chatContext?.ToolApprovalRequestedThisTurn == true;

            // A blank answer is not an answer, unless the run is waiting for the person (an approval card, a contact form) or
            // already delivered its output another way (e.g. a generated image posted to the chat).
            if (string.IsNullOrWhiteSpace(runnableAgentData.Message) && !awaitingApproval && _chatContext?.ContactFormRequestedThisTurn != true
                && !_chatTurnFailureState.AnswerDelivered)
                return Failed(command, ChatErrorCodes.ChatModelEmptyResponse);

            var result = new AgentInvocationResult
            {
                AgentRole = command.AgentRole,
                AgentId = command.Id,
                Success = true,
                Content = runnableAgentData.Message,
                AwaitingApproval = awaitingApproval,
                CompletedAtUtc = _dateTimeProvider.DateTimeOffsetUtcNow(),
                Metadata = new Dictionary<string, string>
                {
                    [nameof(command.SessionId)] = command.SessionId
                }
            };

            return new SuccessResult<AgentInvocationResult>(result);
        }

        private SuccessResult<AgentInvocationResult> Failed(InvokeAgentCommand command, string errorCode) =>
            new(new AgentInvocationResult
            {
                AgentRole = command.AgentRole,
                AgentId = command.Id,
                Success = false,
                ErrorCode = ChatErrorCodes.Normalize(errorCode),
                ErrorMessage = $"The agent run failed ({ChatErrorCodes.Normalize(errorCode)}).",
                CompletedAtUtc = _dateTimeProvider.DateTimeOffsetUtcNow(),
                Metadata = new Dictionary<string, string>
                {
                    [nameof(command.SessionId)] = command.SessionId
                }
            });
    }
}
