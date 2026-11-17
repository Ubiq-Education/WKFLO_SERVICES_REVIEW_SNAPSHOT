using AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitPublicAgentContactForm
{
    public class SubmitPublicAgentContactFormCommandHandler : IRequestHandler<SubmitPublicAgentContactFormCommand, Result>
    {
        private readonly IWkfloMediator _mediator;
        private readonly TenantContext _tenantContext;
        private readonly IChatNotificationService _notificationService;
        private readonly IContactFormReplayStateService _contactFormReplayStateService;

        public SubmitPublicAgentContactFormCommandHandler(IWkfloMediator mediator, TenantContext tenantContext, IChatNotificationService notificationService,
            IContactFormReplayStateService contactFormReplayStateService)
        {
            _mediator = mediator;
            _tenantContext = tenantContext;
            _notificationService = notificationService;
            _contactFormReplayStateService = contactFormReplayStateService;
        }

        public async ValueTask<Result> Handle(SubmitPublicAgentContactFormCommand command, CancellationToken cancellationToken)
        {
            await _mediator.Publish(new ContactFormSubmittedDomainEvent(
                _tenantContext.TenantName,
                command.SubjectId,
                command.SessionId,
                command.FirstName,
                command.LastName,
                command.Email,
                shareId: command.ShareId), cancellationToken);

            if (!string.IsNullOrEmpty(command.OriginalMessage))
            {
                // Record the pending replay BEFORE notifying the client, so the flag is already valid by
                // the time a client-triggered replay request can possibly arrive — see
                // IContactFormReplayStateService for why this exists (the client-supplied
                // IsContactFormReplay flag alone is not a trustworthy signal).
                await _contactFormReplayStateService.SetPendingReplayAsync(command.SessionId, command.OriginalMessage, cancellationToken);
                await _notificationService.SendContactFormCompletedAsync(command.SessionId, command.OriginalMessage);
            }

            return new SuccessResult<bool>(true);
        }
    }
}
