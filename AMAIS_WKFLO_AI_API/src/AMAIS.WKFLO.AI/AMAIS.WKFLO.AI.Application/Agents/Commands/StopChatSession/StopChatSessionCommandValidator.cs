using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Common.Validators;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7501: same tenant/agent checks as the tenant chat message endpoint, plus the session ownership check.</summary>
    public class StopTenantChatSessionCommandValidator : BaseAIValidator<StopTenantChatSessionCommand>
    {
        private readonly UserContext _userContext;
        private readonly IChatSessionOwnerStore _ownerStore;
        private readonly ILogger<StopTenantChatSessionCommandValidator> _logger;

        public StopTenantChatSessionCommandValidator(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext,
            IChatSessionOwnerStore ownerStore, ILogger<StopTenantChatSessionCommandValidator> logger) : base(db, tenantContext)
        {
            _userContext = userContext;
            _ownerStore = ownerStore;
            _logger = logger;

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

            RuleFor(m => m)
               .MustAsync((command, cancellationToken) => StopChatSessionOwnership.IsSessionOfThisChatAsync(
                   _db, _tenantContext.TenantName, command.Id, _userContext?.Guid, command.SessionId, cancellationToken, _ownerStore, _logger))
               .WithMessage(ErrorConst.ChatSessionNotOwned)
               .WithName(nameof(StopChatSessionCommandBase.SessionId));
        }
    }

    /// <summary>US 7501: same tenant/share id checks as the public chat message endpoint, plus the session ownership check.</summary>
    public class StopPublicChatSessionCommandValidator : BaseAIValidator<StopPublicChatSessionCommand>
    {
        private readonly UserContext _userContext;
        private readonly IChatSessionOwnerStore _ownerStore;
        private readonly ILogger<StopPublicChatSessionCommandValidator> _logger;

        public StopPublicChatSessionCommandValidator(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext,
            IChatSessionOwnerStore ownerStore, ILogger<StopPublicChatSessionCommandValidator> logger) : base(db, tenantContext)
        {
            _userContext = userContext;
            _ownerStore = ownerStore;
            _logger = logger;

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

            RuleFor(m => m)
               .MustAsync(BeSessionOfThisShareAsync)
               .WithMessage(ErrorConst.ChatSessionNotOwned)
               .WithName(nameof(StopChatSessionCommandBase.SessionId));
        }

        private async Task<bool> BeSessionOfThisShareAsync(StopPublicChatSessionCommand command, CancellationToken cancellationToken)
        {
            var hostAgentId = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(m => m.ShareId == command.ShareId && !m.IsDeleted && m.Agent.Tenant.Name == _tenantContext.TenantName)
                .Select(m => (int?)m.AgentId)
                .FirstOrDefaultAsync(cancellationToken);

            return await StopChatSessionOwnership.IsSessionOfThisChatAsync(
                _db, _tenantContext.TenantName, hostAgentId, _userContext?.Guid, command.SessionId, cancellationToken, _ownerStore, _logger);
        }
    }

    /// <summary>
    /// US 7501 (defence in depth): a Stop is already scoped by (tenant, session), but a session id is chosen by the
    /// client. When the session already has conversation rows, they must belong to the caller: its tenant, its chat
    /// agent (or agents that agent reaches through hand-offs, directly or not, when only they have saved so far) and its
    /// user: when a row belongs to a known user, only that user may stop the session (an anonymous caller may not).
    /// Sessions whose rows have no known user (anonymous widget or share chats) stay open to anonymous callers.
    /// A session with no row yet (a first turn still in progress) is checked against the owner its first message
    /// recorded (<see cref="IChatSessionOwnerStore"/>), with the same agent and user rules.
    /// </summary>
    internal static class StopChatSessionOwnership
    {
        public static async Task<bool> IsSessionOfThisChatAsync(IMasterDataDbContext db, string tenantName, int? hostAgentId, string userGuid,
            string sessionId, CancellationToken cancellationToken, IChatSessionOwnerStore ownerStore = null, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return true;

            var rows = await db.Conversations
                .AsNoTracking()
                .Where(c => c.SessionId == sessionId)
                .Select(c => new { c.AgentId, c.UserId, TenantName = c.Tenant.Name })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                return await IsFirstTurnOfThisChatAsync(db, ownerStore, logger, tenantName, hostAgentId, userGuid, sessionId, cancellationToken);

            if (hostAgentId is not int host)
                return false;

            // Kept although the Stop itself is tenant-scoped: some state the Stop touches is keyed by session only (the
            // contact form replay key, the SignalR group named after the session), so a session that already belongs to
            // another tenant is never stopped from here.
            if (rows.Any(r => !string.Equals(r.TenantName?.Trim(), tenantName?.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;

            // The rows that tell who owns the session: the chat agent's own, or (before it saved) its hand-off agents'.
            var hostRows = rows.Where(r => r.AgentId == host).ToList();
            var ownerRows = hostRows.Count > 0 ? hostRows : rows;

            // A known user owns the session: only that user may stop it (never an anonymous caller).
            var knownOwners = ownerRows.Where(r => IsKnownUser(r.UserId)).ToList();
            if (knownOwners.Count > 0
                && (!IsKnownUser(userGuid) || knownOwners.Any(r => !string.Equals(r.UserId, userGuid, StringComparison.OrdinalIgnoreCase))))
                return false;

            if (hostRows.Count > 0)
                return true;

            // Only hand-off agents have saved so far: each must be reachable from the chat agent, directly or through
            // other hand-offs (host -> B -> C).
            var reachable = await HandOffTargetClosure.ExpandAsync(db, tenantName, [host], cancellationToken);
            return rows.All(r => reachable.Contains(r.AgentId));
        }

        /// <summary>
        /// No conversation row yet: the session must have been started by this chat (its agent, or an agent it hands off
        /// to) and, when that was a known user, by this same user. No recorded owner means no: another chat or user of
        /// the tenant could otherwise stop a first turn just by knowing its session id.
        /// </summary>
        private static async Task<bool> IsFirstTurnOfThisChatAsync(IMasterDataDbContext db, IChatSessionOwnerStore ownerStore, ILogger logger,
            string tenantName, int? hostAgentId, string userGuid, string sessionId, CancellationToken cancellationToken)
        {
            if (hostAgentId is not int host || ownerStore == null)
                return false;

            ChatSessionOwner owner;
            try
            {
                owner = await ownerStore.GetAsync(tenantName, sessionId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The owner cannot be read (state store down), so no mismatch can be shown: allowed, as before this check.
                // The Stop stays scoped to the caller's tenant. Refusing would make every first-turn Stop fail while the
                // store is down, which is the case the Stop exists for.
                logger?.LogWarning(ex, "Reading the owner of session {SessionId} failed; allowing the Stop of its first turn", sessionId);
                return true;
            }

            if (owner == null)
                return false;

            // A known user started the session: only that user may stop it (never an anonymous caller).
            if (IsKnownUser(owner.UserId)
                && (!IsKnownUser(userGuid) || !string.Equals(owner.UserId, userGuid, StringComparison.OrdinalIgnoreCase)))
                return false;

            var ownerAgentId = owner.AgentId;
            if (ownerAgentId == null && !string.IsNullOrWhiteSpace(owner.ShareId))
            {
                ownerAgentId = await db.AgentChatSettings
                    .AsNoTracking()
                    .Where(m => m.ShareId == owner.ShareId && !m.IsDeleted && m.Agent.Tenant.Name == tenantName)
                    .Select(m => (int?)m.AgentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (ownerAgentId is not int ownerAgent)
                return false;

            if (ownerAgent == host)
                return true;

            var reachable = await HandOffTargetClosure.ExpandAsync(db, tenantName, [host], cancellationToken);
            return reachable.Contains(ownerAgent);
        }

        private static bool IsKnownUser(string userGuid) =>
            !string.IsNullOrWhiteSpace(userGuid) && !string.Equals(userGuid, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
