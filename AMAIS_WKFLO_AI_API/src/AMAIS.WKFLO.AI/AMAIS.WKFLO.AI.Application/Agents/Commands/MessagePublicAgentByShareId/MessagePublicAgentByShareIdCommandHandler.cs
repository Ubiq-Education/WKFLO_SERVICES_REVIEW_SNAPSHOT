using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId
{
    public class MessagePublicAgentByShareIdCommandHandler : IRequestHandler<MessagePublicAgentByShareIdCommand, Result>
    {
        private readonly IPublicAISyncService _publicAISyncService;
        private readonly IWkfloMapper _mapper;
        private readonly IContactFormReplayStateService _contactFormReplayStateService;
        private readonly ChatContext _chatContext;
        private readonly ToolApprovalSessionGate _toolApprovalSessionGate;

        public MessagePublicAgentByShareIdCommandHandler(IPublicAISyncService publicAISyncService, IWkfloMapper mapper,
            IContactFormReplayStateService contactFormReplayStateService, ChatContext chatContext, ToolApprovalSessionGate toolApprovalSessionGate = null)
        {
            _chatContext = chatContext;
            _toolApprovalSessionGate = toolApprovalSessionGate;
            _publicAISyncService = publicAISyncService;
            _mapper = mapper;
            _contactFormReplayStateService = contactFormReplayStateService;
        }

        public async ValueTask<Result> Handle(MessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken)
        {
            // Reaching here means the command validator already confirmed a valid pending replay exists
            // for this session (see MessagePublicAgentByShareIdCommandValidator) — read it BEFORE consuming
            // it (single-use) so its OriginalMessageId (US 7142), when present, can be threaded through to
            // reuse the original question's MessageId on this replay turn instead of a freshly-generated
            // one (see RunTextOptions.OriginalMessageId's own doc comment for why).
            Guid? originalMessageId = null;
            if (command.IsContactFormReplay)
            {
                var pending = await _contactFormReplayStateService.GetPendingReplayAsync(command.SessionId, cancellationToken);
                originalMessageId = pending?.OriginalMessageId;
                await _contactFormReplayStateService.ConsumePendingReplayAsync(command.SessionId, cancellationToken);
            }

            // WI 7575: whoever sends this message is chatting live and can answer a tool approval request.
            _chatContext.ToolApprovalsAvailable = true;

            // WI 7575: sending a new message moves on from any approval still waiting in this session - it expires
            // (= denied) so a stale stored call can never run later.
            if (_toolApprovalSessionGate != null)
                await _toolApprovalSessionGate.ExpireOpenApprovalsAsync(command.SessionId, cancellationToken);

            var tenantAgentData = await _publicAISyncService.RunPublicAgentByShareIdAsync(command.ShareId, command.Input.Text, command.Input.Files, command.SessionId,
                isContactFormReplay: command.IsContactFormReplay, originalMessageId: originalMessageId, cancellationToken: cancellationToken);
            return tenantAgentData == null ? new NotFoundResult() : new SuccessResult<PublicAgentDataResult>(_mapper.Map<PublicAgentDataResult>(tenantAgentData));
        }
    }
}
