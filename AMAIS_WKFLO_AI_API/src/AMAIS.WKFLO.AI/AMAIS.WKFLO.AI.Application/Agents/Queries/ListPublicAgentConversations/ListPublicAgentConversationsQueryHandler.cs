using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Helpers;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentConversations
{
    /// <summary>
    /// US 7507: lists the caller's MAISY conversations, newest last activity first.
    /// <para>
    /// A conversation is a chat session. It has one Conversation row per agent that took part in it (MAISY's own
    /// row plus one per spun-up agent, see UpdateShortTermMemoryChatHistoryUpdatedDomainEventHandler). A session
    /// belongs to the caller when MAISY's row carries the caller's WKFLO user GUID, the same GUID the chat blob
    /// path uses. Runs a fixed number of queries, whatever the number of conversations or agents.
    /// </para>
    /// </summary>
    public class ListPublicAgentConversationsQueryHandler : IRequestHandler<ListPublicAgentConversationsQuery, Result>
    {
        public const int MaxConversations = 50;
        public const int MaxAgeInDays = 90;
        public const int MaxRoleLength = 150;

        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public ListPublicAgentConversationsQueryHandler(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext, IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(ListPublicAgentConversationsQuery query, CancellationToken cancellationToken)
        {
            // Anonymous visitors (no Amais JWT) all share Guid.Empty as their user GUID: listing "their"
            // conversations would list every anonymous visitor's conversations.
            var userId = _userContext.Guid;
            if (string.IsNullOrEmpty(userId) || userId == Guid.Empty.ToString())
                return Empty();

            var rootAgent = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => acs.ShareId == query.ShareId && !acs.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                .Select(acs => new { acs.AgentId, acs.IsWidget, acs.Agent.TenantId })
                .FirstOrDefaultAsync(cancellationToken);

            if (rootAgent == null)
                return new NotFoundResult(nameof(query.ShareId), ErrorConst.AgentNotFound);

            if (!await AgentTagHelper.IsMaisyAgentAsync(_db, rootAgent.AgentId, cancellationToken))
                return Empty();

            var since = _dateTimeProvider.DateTimeOffsetUtcNow().AddDays(-MaxAgeInDays);

            var callerSessionIds = _db.Conversations
                .Where(c => c.TenantId == rootAgent.TenantId
                         && c.AgentId == rootAgent.AgentId
                         && c.UserId == userId
                         && !c.IsTestChat
                         && !c.IsDeleted)
                .Select(c => c.SessionId);

            // Last activity spans every agent's row: a spun-up agent finishing later counts as activity too.
            var sessions = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.TenantId == rootAgent.TenantId && !c.IsDeleted && callerSessionIds.Contains(c.SessionId))
                .GroupBy(c => c.SessionId)
                .Select(g => new
                {
                    SessionId = g.Key,
                    LastActivityAt = g.Max(c => c.LastActivityAt ?? c.LastUpdatedOn ?? c.CreatedOn)
                })
                .Where(s => s.LastActivityAt >= since)
                .OrderByDescending(s => s.LastActivityAt)
                .Take(MaxConversations)
                .ToListAsync(cancellationToken);

            if (sessions.Count == 0)
                return Empty();

            var sessionIds = sessions.Select(s => s.SessionId).ToList();

            var rows = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.TenantId == rootAgent.TenantId && !c.IsDeleted && sessionIds.Contains(c.SessionId))
                .Select(c => new { c.SessionId, c.AgentId, c.Name, c.PreviewLine, c.CreatedOn })
                .ToListAsync(cancellationToken);

            var agentIds = rows.Select(r => r.AgentId).Distinct().ToList();

            // Agents (not TenantAgents): in the public flow MAISY and the agents it spins up are
            // PublishedTenantAgents. Deleted agents are filtered out globally and simply drop out of the avatars.
            var agents = await _db.Agents
                .AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name, a.Description, a.IconUrl })
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            var chatSettings = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => agentIds.Contains(acs.AgentId) && !acs.IsDeleted)
                .Select(acs => new { acs.AgentId, acs.IsWidget, acs.ChatbotIconPath, acs.LastUpdatedOn, acs.CreatedOn })
                .ToListAsync(cancellationToken);

            // Same pick as ListPublicAgentsChatMessagesBySessionIdQueryHandler: an agent can have several chat
            // settings (widget / non-widget); prefer the one in the root agent's context, then the latest one.
            var chatbotIconUrls = chatSettings
                .GroupBy(acs => acs.AgentId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(acs => acs.IsWidget == rootAgent.IsWidget)
                        .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                        .First()
                        .ChatbotIconPath);

            var rowsBySession = rows.ToLookup(r => r.SessionId);

            var results = sessions.Select(session =>
            {
                var sessionRows = rowsBySession[session.SessionId].ToList();
                var rootRow = sessionRows.FirstOrDefault(r => r.AgentId == rootAgent.AgentId);

                var participants = sessionRows
                    .GroupBy(r => r.AgentId)
                    .Select(g => new { AgentId = g.Key, JoinedOn = g.Min(r => r.CreatedOn) })
                    .Where(p => agents.ContainsKey(p.AgentId))
                    .OrderByDescending(p => p.AgentId == rootAgent.AgentId)
                    .ThenBy(p => p.JoinedOn)
                    .ThenBy(p => p.AgentId)
                    .Select(p =>
                    {
                        var agent = agents[p.AgentId];
                        chatbotIconUrls.TryGetValue(p.AgentId, out var chatbotIconUrl);

                        return new PublicAgentConversationAgentResult
                        {
                            AgentId = agent.Id,
                            Name = agent.Name,
                            Role = Truncate(agent.Description, MaxRoleLength),
                            AvatarUrl = NullIfEmpty(chatbotIconUrl) ?? NullIfEmpty(agent.IconUrl)
                        };
                    })
                    .ToList();

                return new PublicAgentConversationResult
                {
                    SessionId = session.SessionId,
                    Title = NullIfEmpty(rootRow?.Name),
                    PreviewLine = NullIfEmpty(rootRow?.PreviewLine),
                    LastActivityAt = session.LastActivityAt,
                    Agents = participants
                };
            }).ToList();

            return new SuccessResult<List<PublicAgentConversationResult>>(results);
        }

        private static SuccessResult<List<PublicAgentConversationResult>> Empty() => new(new List<PublicAgentConversationResult>());

        private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string Truncate(string value, int maxLength)
        {
            value = NullIfEmpty(value)?.Trim();
            if (value == null || value.Length <= maxLength)
                return value;

            return value[..(maxLength - 1)].TrimEnd() + "…";
        }
    }
}
