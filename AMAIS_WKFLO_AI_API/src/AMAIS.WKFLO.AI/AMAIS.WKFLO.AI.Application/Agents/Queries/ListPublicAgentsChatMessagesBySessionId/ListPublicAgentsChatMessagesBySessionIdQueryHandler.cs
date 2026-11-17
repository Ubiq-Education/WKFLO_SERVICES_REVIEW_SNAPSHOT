using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListPublicAgentsChatMessagesBySessionId
{
    public class ListPublicAgentsChatMessagesBySessionIdQueryHandler : IRequestHandler<ListPublicAgentsChatMessagesBySessionIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListPublicAgentsChatMessagesBySessionIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListPublicAgentsChatMessagesBySessionIdQuery query, CancellationToken cancellationToken)
        {
            // ShareId (validated upstream by WebChatAuthorizationAttribute) is only used to authorize the
            // request — by the time this handler runs, _tenantContext.TenantName is already resolved from
            // the request's tenant header, same as ListPublicAgentChatMessagesQueryHandler relies on today.
            var participatingAgentIds = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.SessionId == query.SessionId && c.Tenant.Name == _tenantContext.TenantName)
                .Select(c => c.AgentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (participatingAgentIds.Count == 0)
                return new SuccessResult<List<PublicAgentChatMessageResult>>(new List<PublicAgentChatMessageResult>());

            // Resolves the one agent the caller actually shared/opened — every other participating agent
            // in this session got here via a spin-up hand-off (see AgentEventPublisherPlugin). A null here
            // means resolution failed (should not happen since ShareId is validated upstream); fail open
            // rather than stripping messages from every agent. ShareId unambiguously identifies a single
            // AgentChatSetting row, so its own IsWidget value IS the root's widget/non-widget context —
            // no ambiguity here, unlike the tenant handler which has no ShareId to key off of.
            var mainAgentSetting = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => acs.ShareId == query.ShareId && !acs.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                .Select(acs => new { acs.AgentId, acs.IsWidget })
                .FirstOrDefaultAsync(cancellationToken);

            var mainAgentId = mainAgentSetting?.AgentId;
            var rootIsWidget = mainAgentSetting?.IsWidget;

            // Conversation.Agent is typed as TenantAgent specifically, not the polymorphic base Agent — the
            // public flow's participants are PublishedTenantAgents, so names must be looked up separately
            // against the root Agents DbSet rather than relying on that navigation.
            var agentNames = await _db.Agents
                .AsNoTracking()
                .Where(a => participatingAgentIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

            // Same per-agent icon reused for both bubble and messages — AgentChatSetting is the
            // existing storage for it, no new entity/migration needed.
            // AgentId is NOT unique on this table (e.g. separate widget vs. non-widget configs —
            // see GetTenantAgentChatSettingQueryHandler), so materialize and pick one row per agent
            // rather than assuming uniqueness with ToDictionaryAsync.
            var agentChatSettingRows = await _db.AgentChatSettings
                .AsNoTracking()
                .Where(acs => participatingAgentIds.Contains(acs.AgentId) && !acs.IsDeleted)
                .ToListAsync(cancellationToken);

            // Every participating agent's icon is resolved in the same widget/non-widget context as
            // the root (rootIsWidget above) rather than each agent independently defaulting to
            // "prefer widget" — falling back to that agent's own most-recently-updated row when it
            // has no row matching the root's IsWidget value (or when rootIsWidget is null).
            var agentChatIconUrls = agentChatSettingRows
                .GroupBy(acs => acs.AgentId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(acs => rootIsWidget.HasValue && acs.IsWidget == rootIsWidget.Value)
                        .ThenByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                        .First()
                        .ChatbotIconPath);

            var perAgentMessages = await Task.WhenAll(participatingAgentIds.Select(async agentId =>
            {
                var (_, _, chatHistory, messageRatings) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, agentId, query.SessionId, _userContext.Guid);
                var messages = _mapper.Map<List<PublicAgentChatMessageResult>>(chatHistory);
                ApplyRatings(messages, messageRatings);

                agentNames.TryGetValue(agentId, out var agentName);
                agentChatIconUrls.TryGetValue(agentId, out var chatbotIconUrl);
                foreach (var message in messages)
                {
                    message.AgentId = agentId;
                    message.AgentName = agentName;
                    message.ChatbotIconUrl = chatbotIconUrl;
                }

                RemoveSpinUpAgentUserMessages(messages, agentId, mainAgentId);

                return messages;
            }));

            var allMessages = perAgentMessages.SelectMany(m => m).OrderBy(m => m.Timestamp).ToList();

            // US 7502: each agent stopped by the same Stop saved its own "Stopped" divider; show it once.
            StoppedTurnMarker.RemoveDuplicateMarkers(allMessages, m => m.Metadata);

            return new SuccessResult<List<PublicAgentChatMessageResult>>(allMessages);
        }

        private static void ApplyRatings(List<PublicAgentChatMessageResult> messages, Dictionary<string, string> messageRatings)
        {
            if (messageRatings?.Count is null or 0) return;

            foreach (var message in messages.Where(m => m.MessageId != null))
            {
                if (messageRatings.TryGetValue(message.MessageId!, out var rating))
                    message.Rating = rating;
            }
        }

        // Spin-up agents' hand-off "user" turn is an internal trigger, not a genuine end-user message —
        // hide it, but keep their assistant reply. A null mainAgentId means resolution failed (should not
        // happen since ShareId is validated upstream); fail open rather than stripping every agent's messages.
        //
        // Exception: a message flagged RenderAsSystem is a "record" the plugin authored under
        // AuthorRole.User for API-compatibility reasons, not something the human actually typed — e.g.
        // ImageGenerationPlugin.BuildImageMessage puts a spun-up agent's generated/edited images under
        // User because Anthropic rejects an image content block inside an assistant turn outright.
        // Removing it here would silently hide every image a spun-up agent generates from this view.
        private static void RemoveSpinUpAgentUserMessages(List<PublicAgentChatMessageResult> messages, int agentId, int? mainAgentId)
        {
            if (mainAgentId is int resolvedMainAgentId && agentId != resolvedMainAgentId)
                messages.RemoveAll(m => m.Type == "user" && !HasRenderAsSystem(m.Metadata));
        }

        // Metadata survives a blob JSON round-trip as System.Text.Json.JsonElement rather than its
        // original CLR type — same tolerant-cast pattern used elsewhere in this handler.
        private static bool HasRenderAsSystem(Dictionary<string, object> metadata)
        {
            if (metadata == null || !metadata.TryGetValue(CommonMetadataConsts.RenderAsSystem, out var value) || value == null)
                return false;

            return value switch
            {
                bool b => b,
                System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.False => false,
                string s when bool.TryParse(s, out var parsed) => parsed,
                _ => false
            };
        }
    }
}
