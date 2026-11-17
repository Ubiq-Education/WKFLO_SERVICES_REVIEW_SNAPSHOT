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

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.ListTenantAgentsChatMessagesBySessionId
{
    public class ListTenantAgentsChatMessagesBySessionIdQueryHandler : IRequestHandler<ListTenantAgentsChatMessagesBySessionIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public ListTenantAgentsChatMessagesBySessionIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _mapper = mapper;
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(ListTenantAgentsChatMessagesBySessionIdQuery query, CancellationToken cancellationToken)
        {
            // Conversation is upserted per (AgentId, SessionId) on every chat save (see SyncConversationCommand) —
            // the authoritative, already-indexed index of "which agents participated in this session."
            var participatingAgentIds = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.SessionId == query.SessionId && c.Tenant.Name == _tenantContext.TenantName)
                .Select(c => c.AgentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (participatingAgentIds.Count == 0)
                return new SuccessResult<List<TenantAgentChatMessageResult>>(new List<TenantAgentChatMessageResult>());

            // Conversation.Agent is typed as TenantAgent specifically, not the polymorphic base Agent — it
            // can't be relied on to resolve a PublishedTenantAgent's name, so look names up separately
            // against the root Agents DbSet, the same TPH-safe pattern used elsewhere in this story.
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

            // Whether this request is happening in a "widget" context is a property of the root
            // agent (query.Id) — the one the caller is actually viewing right now — not something
            // each participating (sub-)agent should independently guess. There's no widget/non-widget
            // signal on the route or query itself, so resolve it from the root's own AgentChatSetting
            // row(s), tie-broken by most recently updated if the root also has more than one. Every
            // participating agent's icon is then picked to match that same context, falling back to
            // its own most-recently-updated row when it has no row matching the root's IsWidget value
            // (or when the root has no row at all, i.e. rootIsWidget is null).
            var rootIsWidget = agentChatSettingRows
                .Where(acs => acs.AgentId == query.Id)
                .OrderByDescending(acs => acs.LastUpdatedOn ?? acs.CreatedOn)
                .Select(acs => (bool?)acs.IsWidget)
                .FirstOrDefault();

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
                var messages = _mapper.Map<List<TenantAgentChatMessageResult>>(chatHistory);
                ApplyRatings(messages, messageRatings);

                agentNames.TryGetValue(agentId, out var agentName);
                agentChatIconUrls.TryGetValue(agentId, out var chatbotIconUrl);
                foreach (var message in messages)
                {
                    message.AgentId = agentId;
                    message.AgentName = agentName;
                    message.ChatbotIconUrl = chatbotIconUrl;
                }

                RemoveSpinUpAgentUserMessages(messages, agentId, query.Id);

                return messages;
            }));

            var allMessages = perAgentMessages.SelectMany(m => m).OrderBy(m => m.Timestamp).ToList();

            // US 7502: each agent stopped by the same Stop saved its own "Stopped" divider; show it once.
            StoppedTurnMarker.RemoveDuplicateMarkers(allMessages, m => m.Metadata);

            return new SuccessResult<List<TenantAgentChatMessageResult>>(allMessages);
        }

        private static void ApplyRatings(List<TenantAgentChatMessageResult> messages, Dictionary<string, string> messageRatings)
        {
            if (messageRatings?.Count is null or 0) return;

            foreach (var message in messages.Where(m => m.MessageId != null))
            {
                if (messageRatings.TryGetValue(message.MessageId!, out var rating))
                    message.Rating = rating;
            }
        }

        // Spin-up agents' hand-off "user" turn is an internal trigger, not a genuine end-user message —
        // hide it, but keep their assistant reply. mainAgentId is the agent the caller is actually viewing;
        // every other participating agent got here via a spin-up hand-off.
        //
        // Exception: a message flagged RenderAsSystem is a "record" the plugin authored under
        // AuthorRole.User for API-compatibility reasons, not something the human actually typed — e.g.
        // ImageGenerationPlugin.BuildImageMessage puts Vera's generated/edited images under User
        // because Anthropic rejects an image content block inside an assistant turn outright. Removing
        // it here would silently hide every image a spun-up agent generates from this merged view.
        private static void RemoveSpinUpAgentUserMessages(List<TenantAgentChatMessageResult> messages, int agentId, int mainAgentId)
        {
            if (agentId != mainAgentId)
                messages.RemoveAll(m => m.Type == "user" && !HasRenderAsSystem(m.Metadata));
        }

        // Metadata survives a blob JSON round-trip as System.Text.Json.JsonElement rather than its
        // original CLR type — same tolerant-cast pattern used elsewhere in this handler
        // (GetTimestampFromMetadata / GetChannelMessageTypeFromMetadata on TenantAgentChatMessageResult).
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
