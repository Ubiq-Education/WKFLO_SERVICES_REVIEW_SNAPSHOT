using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>WI 7575: starts the resume turn after a recorded tool approval decision (seam for testability).</summary>
    public interface IToolApprovalResumeQueue
    {
        /// <summary>
        /// Starts the resume turn of <paramref name="record"/> and returns without waiting for it to finish. The resume
        /// runs with the conversation's own identity and chat context taken from the record.
        /// </summary>
        Task EnqueueAsync(ResumeToolApprovalCommand command, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalRecord record, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Runs the resume turn in-process, in the background, in its own DI scope with a snapshot of the decision
    /// request's contexts (tenant, chat/session, user, share id) - the same context restore the queued chat
    /// workflows and <c>AgentInvoker</c> do. The decision endpoint returns right away and the turn streams to the
    /// chat over SignalR exactly like an ordinary turn.
    /// </summary>
    /// <remarks>
    /// Multi-instance safe without a broker hop: only the single caller that won the decision (ETag write in
    /// <c>IToolApprovalStateService.TryDecideAsync</c>) ever starts a resume, on the instance that served it.
    /// This replaced a Dapr pub/sub + workflow hop whose failures were swallowed at every step, which made a
    /// resume that never ran indistinguishable from one that did. Every failure here is logged.
    /// US 7959 / Task 7963: and reported to the chat with a code (once per approval, see <see cref="ToolApprovalResumeFailure"/>).
    /// </remarks>
    public class BackgroundToolApprovalResumeQueue : IToolApprovalResumeQueue
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly ILogger<BackgroundToolApprovalResumeQueue> _logger;

        public BackgroundToolApprovalResumeQueue(IServiceScopeFactory scopeFactory, IWkfloMapper mapper, TenantContext tenantContext, ChatContext chatContext,
            UserContext userContext, AgentSharableContext agentSharableContext, ILogger<BackgroundToolApprovalResumeQueue> logger)
        {
            _scopeFactory = scopeFactory;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _agentSharableContext = agentSharableContext;
            _logger = logger;
        }

        /// <summary>The running resume (exposed for tests); production callers do not await it.</summary>
        internal Task LastResume { get; private set; } = Task.CompletedTask;

        public Task EnqueueAsync(ResumeToolApprovalCommand command, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(record);

            // Snapshot now: the request's scoped contexts are gone once the decision response is sent. The
            // conversation's identity (tenant, user, session, share) and its chat context come from the RECORD - the
            // turn that asked - so the resume uses the same chat history blob and page/feature context, whatever the
            // decision request carried. The request only contributes the caller's credentials/claims (e.g. for a
            // per-user MCP OAuth connection), and the decision rules already require it to be the same user.
            var tenant = _mapper.Map<TenantContext>(_tenantContext ?? new TenantContext());
            tenant.TenantName = record.TenantName;
            var user = _mapper.Map<UserContext>(_userContext ?? new UserContext());
            user.Guid = record.UserId;
            var sharable = _mapper.Map<AgentSharableContext>(_agentSharableContext ?? new AgentSharableContext());
            sharable.ShareId = record.ShareId;
            var chat = (record.Chat ?? new AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalChatState()).ToChatContext(record.SessionId);

            var snapshot = new ContextSnapshot(tenant, chat, user, sharable);

            // Deliberately not tied to the request's cancellation token: the turn must outlive the HTTP request.
            LastResume = Task.Run(() => RunAsync(command, record, snapshot), CancellationToken.None);
            return Task.CompletedTask;
        }

        private async Task RunAsync(ResumeToolApprovalCommand command, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalRecord record, ContextSnapshot snapshot)
        {
            DateTimeOffset? startedAt = null;
            IChatTurnFailureState failureState = null;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                startedAt = services.GetService<IDateTimeProvider>()?.DateTimeOffsetUtcNow();
                // Read after the scope ends (a plain object): whether the answer was delivered before the failure.
                failureState = services.GetService<IChatTurnFailureState>();
                var mapper = services.GetRequiredService<IWkfloMapper>();
                mapper.Map(snapshot.Tenant, services.GetRequiredService<TenantContext>());
                mapper.Map(snapshot.Chat, services.GetRequiredService<ChatContext>());
                mapper.Map(snapshot.User, services.GetRequiredService<UserContext>());
                mapper.Map(snapshot.Sharable, services.GetRequiredService<AgentSharableContext>());

                _logger.LogInformation("Resuming conversation {SessionId} after tool approval {ApprovalId}", command.SessionId, command.ApprovalId);
                var result = await services.GetRequiredService<IWkfloMediator>().Send(command, CancellationToken.None);
                _logger.LogInformation("Tool approval {ApprovalId} resume finished with {ResultType}", command.ApprovalId, result?.ResultType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resuming conversation {SessionId} after tool approval {ApprovalId} failed", command.SessionId, command.ApprovalId);
                await ReportFailureAsync(record, ex, startedAt, failureState?.AnswerDelivered == true);
            }
        }

        /// <summary>
        /// US 7959 / Task 7963: the chat is told the resume failed, with a code (a no-op when the handler already reported it:
        /// same dedupe key). In a fresh scope: the resume's own may be gone or broken.
        /// </summary>
        private async Task ReportFailureAsync(AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalRecord record, Exception exception, DateTimeOffset? startedAt,
            bool answerDelivered)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var code = ChatErrorClassifier.Classify(exception, stopRequested: false) ?? ChatErrorCodes.ChatTurnFailed;
                await ToolApprovalResumeFailure.ReportAsync(services.GetService<IChatAgentFailureReporter>(), services.GetRequiredService<IChatNotificationService>(),
                    record, code, startedAt, _logger, answerDelivered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reporting the failed resume of tool approval {ApprovalId} failed", record.ApprovalId);
            }
        }

        private sealed record ContextSnapshot(TenantContext Tenant, ChatContext Chat, UserContext User, AgentSharableContext Sharable);
    }
}
