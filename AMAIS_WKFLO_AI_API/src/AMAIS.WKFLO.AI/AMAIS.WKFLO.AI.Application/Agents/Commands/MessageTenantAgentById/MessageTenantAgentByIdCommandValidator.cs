using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById
{
    public class MessageTenantAgentByIdCommandValidator : BaseAIValidator<MessageTenantAgentByIdCommand>
    {
        private readonly IContactFormReplayStateService _contactFormReplayStateService;

        public MessageTenantAgentByIdCommandValidator(IMasterDataDbContext db, TenantContext tenantContext,
            IContactFormReplayStateService contactFormReplayStateService) : base(db, tenantContext)
        {
            _contactFormReplayStateService = contactFormReplayStateService;

            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenantAgent)
               .WithMessage(ErrorConst.AgentMustExist)
               .WithName(nameof(Agent));

            // IsContactFormReplay is client-supplied and must never be trusted on its own: `true`
            // suppresses topic classification AND the persisted AuthorRole.User chat-history entry, so an
            // unvalidated flag would let any authenticated tenant caller mark an arbitrary message as a
            // "replay" to have it silently excluded from Chat Logs/analytics. Only honor it when
            // SubmitContactFormCommandHandler genuinely set a pending replay for this exact session (and,
            // defense in depth, the replayed text matches what was originally deferred) — see
            // IContactFormReplayStateService.
            RuleFor(m => m)
                .MustAsync(HaveValidPendingContactFormReplay)
                .WithMessage("No pending ContactForm replay was found for this session — IsContactFormReplay cannot be honored.")
                .WithName(nameof(MessageTenantAgentByIdCommand.IsContactFormReplay))
                .When(m => m.IsContactFormReplay);
        }

        private async Task<bool> HaveValidPendingContactFormReplay(MessageTenantAgentByIdCommand command, CancellationToken cancellationToken)
        {
            var pending = await _contactFormReplayStateService.GetPendingReplayAsync(command.SessionId, cancellationToken);
            return pending != null
                && string.Equals(pending.OriginalMessage?.Trim(), command.Input?.Text?.Trim(), StringComparison.Ordinal);
        }
    }
}
