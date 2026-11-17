using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval
{
    /// <summary>
    /// WI 7575: shared decision logic. A decision is accepted only from the same tenant and session, for the agent
    /// the endpoint addresses, and by the same identity that made the request: an approval requested by a signed-in
    /// user only by that user, an anonymous one only by an anonymous caller (see <see cref="ToolApprovalIdentity"/>).
    /// Anything else is a 404 so approval ids cannot be probed. Single-use: 409 when already decided, 410 when expired.
    /// </summary>
    public class ToolApprovalDecisionProcessor
    {
        private readonly IToolApprovalStateService _stateService;
        private readonly IToolApprovalNotifier _notifier;
        private readonly IToolApprovalResumeQueue _resumeQueue;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ToolApprovalDecisionProcessor(IToolApprovalStateService stateService, IToolApprovalNotifier notifier, IToolApprovalResumeQueue resumeQueue,
            TenantContext tenantContext, UserContext userContext, IDateTimeProvider dateTimeProvider)
        {
            _stateService = stateService;
            _notifier = notifier;
            _resumeQueue = resumeQueue;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<Result> DecideAsync(DecideToolApprovalCommandBase command, int? expectedAgentId, CancellationToken cancellationToken)
        {
            if (!ToolApprovalConsts.Decision.IsUserDecision(command.Decision))
                return new InvalidResult(nameof(command.Decision), ErrorConst.ToolApprovalDecisionInvalid);

            var record = await _stateService.GetAsync(command.ApprovalId, cancellationToken);
            if (record == null || !BelongsToCaller(record, command.SessionId, expectedAgentId))
                return new NotFoundResult(nameof(command.ApprovalId), ErrorConst.ToolApprovalNotFound);

            var (outcome, decided) = await _stateService.TryDecideAsync(command.ApprovalId, command.Decision, _dateTimeProvider.DateTimeOffsetUtcNow(), cancellationToken);

            switch (outcome)
            {
                case ToolApprovalDecisionOutcome.NotFound:
                    return new NotFoundResult(nameof(command.ApprovalId), ErrorConst.ToolApprovalNotFound);

                case ToolApprovalDecisionOutcome.AlreadyDecided:
                    return new ConflictResult(nameof(command.ApprovalId), ErrorConst.ToolApprovalAlreadyDecided);

                case ToolApprovalDecisionOutcome.ExpiredNow:
                    // THIS call performed the Pending -> Expired transition: make the chat and the history reflect it.
                    await _notifier.NotifyResolvedAsync(decided);
                    await _notifier.PersistExpiredAsync(decided);
                    return new GoneResult(nameof(command.ApprovalId), ErrorConst.ToolApprovalExpired);

                case ToolApprovalDecisionOutcome.Expired:
                    // Already expired (and already reported) earlier: a late click just gets 410.
                    return new GoneResult(nameof(command.ApprovalId), ErrorConst.ToolApprovalExpired);
            }

            if (decided.Decision == ToolApprovalConsts.Decision.AllowForConversation)
                await _stateService.AddConversationGrantAsync(decided.SessionId, decided.GrantKey, cancellationToken);

            await _notifier.NotifyResolvedAsync(decided);
            await _resumeQueue.EnqueueAsync(new ResumeToolApprovalCommand { ApprovalId = decided.ApprovalId, SessionId = decided.SessionId }, decided, cancellationToken);

            return new SuccessResult<ToolApprovalDecisionResult>(new ToolApprovalDecisionResult
            {
                ApprovalId = decided.ApprovalId,
                Decision = decided.Decision,
                Status = decided.Status
            });
        }

        internal bool BelongsToCaller(ToolApprovalRecord record, string sessionId, int? expectedAgentId) =>
            record.SessionId == sessionId
            && string.Equals(record.TenantName, _tenantContext.TenantName, StringComparison.OrdinalIgnoreCase)
            // Bug 7722: a sub-agent's request is decided from the root agent's chat.
            && (expectedAgentId == null || record.AgentId == expectedAgentId || record.RootAgentId == expectedAgentId)
            && ToolApprovalIdentity.IsSameUser(record.UserId, _userContext?.Guid);
    }

    public class DecideTenantToolApprovalCommandHandler : IRequestHandler<DecideTenantToolApprovalCommand, Result>
    {
        private readonly ToolApprovalDecisionProcessor _processor;

        public DecideTenantToolApprovalCommandHandler(ToolApprovalDecisionProcessor processor)
        {
            _processor = processor;
        }

        public async ValueTask<Result> Handle(DecideTenantToolApprovalCommand command, CancellationToken cancellationToken) =>
            await _processor.DecideAsync(command, command.Id, cancellationToken);
    }

    public class DecidePublicToolApprovalCommandHandler : IRequestHandler<DecidePublicToolApprovalCommand, Result>
    {
        private readonly ToolApprovalDecisionProcessor _processor;
        private readonly IMasterDataDbContext _db;

        public DecidePublicToolApprovalCommandHandler(ToolApprovalDecisionProcessor processor, IMasterDataDbContext db)
        {
            _processor = processor;
            _db = db;
        }

        public async ValueTask<Result> Handle(DecidePublicToolApprovalCommand command, CancellationToken cancellationToken)
        {
            // The share id resolves to the (published) agent that ran the shared chat - same lookup the public
            // chat itself uses (AgentChatSetting.ShareId -> AgentId).
            var agentId = await _db.AgentChatSettings
                .Where(acs => acs.ShareId == command.ShareId && !acs.IsDeleted)
                .Select(acs => (int?)acs.AgentId)
                .FirstOrDefaultAsync(cancellationToken);

            if (agentId == null)
                return new NotFoundResult(nameof(command.ShareId), ErrorConst.AgentNotFound);

            return await _processor.DecideAsync(command, agentId, cancellationToken);
        }
    }
}
