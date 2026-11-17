using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadSiblingAgentContextBySession
{
    public class LoadSiblingAgentContextBySessionQueryHandler : IRequestHandler<LoadSiblingAgentContextBySessionQuery, List<ChatMessageContent>>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IChatStoreService _chatStoreService;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public LoadSiblingAgentContextBySessionQueryHandler(IMasterDataDbContext db, IChatStoreService chatStoreService, IBlobStorageProvider blobStorageProvider,
            TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _chatStoreService = chatStoreService;
            _blobStorageProvider = blobStorageProvider;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<List<ChatMessageContent>> Handle(LoadSiblingAgentContextBySessionQuery query, CancellationToken cancellationToken)
        {
            // Gate: reuses RunnableAgent.EnableSpinUpAgents as-is (story #6480) rather than a new flag —
            // an agent authorized to spin up children is also the agent expected to know those
            // children's histories. No DB query at all when this is false.
            if (query.Agent is not { EnableSpinUpAgents: true })
                return [];

            // Scope: only agents this root is explicitly authorized to spin up (AgentAllowedSpinUpAgent),
            // mirroring the same allowlist lookup SemanticKernelAgentService.BuildAgentEventPublisherInjectionAsync
            // uses for the push direction — never "any other agent present in the session."
            // Inactive children are excluded too (WI 7318 hardening), same as that hand-off catalog:
            // an admin switching an agent off must stop its content flowing into the root's context.
            // Shared, EF-translatable filter (see WhereActiveAllowedSpinUpAgents' remarks).
            var allowedChildren = await Services.SemanticKernel.SemanticKernelAgentService
                .WhereActiveAllowedSpinUpAgents(_db.AgentAllowedSpinUpAgents.AsNoTracking().Cacheable(), query.Agent.Id)
                .Select(m => new { m.AllowedSpinUpAgentId, m.AllowedSpinUpAgent.Name })
                .ToListAsync(cancellationToken);

            if (allowedChildren.Count == 0)
                return [];

            var allowedChildIds = allowedChildren.Select(c => c.AllowedSpinUpAgentId).ToList();

            // Same Conversation(SessionId, AgentId) lookup shape as
            // ListTenantAgentsChatMessagesBySessionIdQueryHandler — only children that actually have a
            // conversation row for this session participated in it. Not cached: conversation
            // participation changes turn-by-turn and must be read fresh.
            var participatingChildIds = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.SessionId == query.SessionId && c.Tenant.Name == _tenantContext.TenantName && allowedChildIds.Contains(c.AgentId))
                .Select(c => c.AgentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (participatingChildIds.Count == 0)
                return [];

            var injectedMessages = new List<ChatMessageContent>();

            foreach (var childId in participatingChildIds)
            {
                var (_, _, childHistory, _) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, childId, query.SessionId, _userContext.Guid);
                if (childHistory == null || childHistory.Count == 0)
                    continue;

                var childName = allowedChildren.First(c => c.AllowedSpinUpAgentId == childId).Name;

                foreach (var message in childHistory)
                {
                    if (!IsInjectableChildMessage(message))
                        continue;

                    var injected = await BuildInjectedMessageAsync(childName, message, cancellationToken);
                    if (injected != null)
                        injectedMessages.Add(injected);
                }
            }

            return injectedMessages
                .OrderBy(GetMessageTimestamp)
                .ToList();
        }

        // Keep only the child's real output — its own reply text and any file/image it generated —
        // and drop everything internal:
        //  - the child's own inbound trigger message (Role == User — e.g. the spin-up hand-off text
        //    MAISY sent Vera), matching the same exclusion ListTenantAgentsChatMessagesBySessionIdQueryHandler
        //    already applies via RemoveSpinUpAgentUserMessages;
        //  - tool-call/tool-result protocol messages (Role == Tool, or Assistant messages carrying only
        //    FunctionCallContent with no real text/image);
        //  - reasoning/"thinking" scratch messages (ChannelMessageType.Thinking — see
        //    SemanticKernelAgentServiceRun's AssitantReasoning/ImageAnalysis injection);
        //  - the "Stopped. All agents cancelled." divider of a stopped turn (US 7502).
        // Generated/edited images — the primary case this story exists for — ride as
        // Role == User with ChannelMessageType.Metadata (see ImageGenerationPlugin.BuildImageMessage;
        // moved off Assistant because Anthropic rejects an 'image' content block inside an assistant
        // turn outright, and off System before that for the mid-conversation-system-message reason).
        // A bare Role == User message with no Metadata channel type is either the child's own inbound
        // trigger (excluded above) or an image the real end user actually uploaded to the child
        // (excluded deliberately — this story is about the child's own output, not re-surfacing the
        // human's own uploads) — ChannelMessageType.Metadata is what tells those apart from a
        // generated/edited image the child produced.
        //
        // Bug 7144: Role == System is legacy backward-compat for images generated before the
        // mid-conversation-system-message fix (persisted as System + Metadata) — it must NOT accept
        // any System-role message unconditionally. This codebase also persists System-role messages
        // for unrelated internal purposes (the pinned chatHistory[0] context message built from
        // PageData/TextFeature/GalleryFeature/Knowledge auto-invoke sources and TopicHandoffMessage —
        // see SemanticKernelExtensions/UpsertSystemMessageSection); without the Metadata gate, one of
        // those would be pulled into the root agent's context as if the child had "shared" it,
        // potentially leaking internal prompt/RAG instructions.
        private static bool IsInjectableChildMessage(ChatMessageContent message)
        {
            var channelType = GetChannelMessageType(message);

            var isEligibleRole = message.Role == AuthorRole.Assistant
                || (message.Role == AuthorRole.System && channelType == ChannelMessageType.Metadata)
                || (message.Role == AuthorRole.User && channelType == ChannelMessageType.Metadata);

            if (!isEligibleRole || channelType == ChannelMessageType.Thinking)
                return false;

            // US 7502: the child's "Stopped" divider is not its output (and is never sent to a model).
            if (StoppedTurnMarker.IsMarker(message))
                return false;

            var hasText = message.Items.OfType<TextContent>().Any(t => !string.IsNullOrWhiteSpace(t.Text));
            var hasImage = message.Items.OfType<ImageContent>().Any();

            return hasText || hasImage;
        }

        private async Task<ChatMessageContent> BuildInjectedMessageAsync(string childAgentName, ChatMessageContent sourceMessage, CancellationToken cancellationToken)
        {
            var items = new ChatMessageContentItemCollection();

            var text = string.Join("\n", sourceMessage.Items.OfType<TextContent>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            var attribution = $"[Context automatically shared by agent '{childAgentName}']";
            items.Add(new TextContent(string.IsNullOrWhiteSpace(text) ? attribution : $"{attribution}: {text}"));

            foreach (var sourceImage in sourceMessage.Items.OfType<ImageContent>())
            {
                var resolvedImage = await ResolveImageBytesAsync(sourceImage, cancellationToken);
                if (resolvedImage != null)
                    items.Add(resolvedImage);
            }

            // Nothing usable came through (e.g. the only image had already been deleted from blob
            // storage) and there was no real text either — skip rather than injecting a bare
            // attribution line with no actual content.
            if (!items.OfType<ImageContent>().Any() && string.IsNullOrWhiteSpace(text))
                return null;

            // Start from the source message's own metadata, not just the two keys this handler cares
            // about — FileName/FileType in particular must survive, or plugins that resolve "the image
            // in chat history" by matching those keys (GalleryFeaturePlugin, PagePlugin, PostPlugin —
            // all query _chatContext.CurrentChatHistory, which includes this injected message for the
            // turn) can no longer find an image the child agent generated. IsEphemeralAgentContext and
            // Timestamp are then set/overwritten on top, same as before.
            var metadata = sourceMessage.Metadata != null
                ? new Dictionary<string, object>(sourceMessage.Metadata)
                : new Dictionary<string, object>();
            metadata[CommonMetadataConsts.IsEphemeralAgentContext] = true;
            metadata[CommonMetadataConsts.Timestamp] = GetMessageTimestamp(sourceMessage) ?? DateTimeOffset.UtcNow;

            // AuthorRole.User, not System or Assistant. System is out because Anthropic rejects a
            // 'system' message anywhere but the very first entry (or right after a
            // server-tool-result-ending turn), and this is appended mid-history. Assistant is also
            // out — separately, Anthropic rejects an 'image' content block inside an assistant turn
            // outright, and this message frequently carries one (the primary case this story exists
            // for: Vera's generated/edited image). Since this message is ephemeral — built fresh every
            // turn from the child's blob and never persisted or shown to any real user (see
            // BuildPersistableChatHistory) — the "would misrepresent this as something the end user
            // said" concern that ruled out User elsewhere doesn't apply here: nothing ever renders it.
            return new ChatMessageContent(AuthorRole.User, items, metadata: metadata);
        }

        // Re-download bytes rather than forwarding the (time-limited) SAS URL LoadChatAsync just
        // generated for this blob — same durability pattern as
        // AgentEventPublisherPlugin.ResolveImageAttachmentAsync, so an injected image doesn't go stale
        // between being loaded here and actually reaching the model/provider.
        private async Task<ImageContent> ResolveImageBytesAsync(ImageContent source, CancellationToken cancellationToken)
        {
            byte[] imageBytes = null;

            if (source.Data.HasValue && source.Data.Value.Length > 0)
            {
                imageBytes = source.Data.Value.ToArray();
            }
            else if (source.Uri != null && source.Uri.AbsolutePath.Contains("/chat-images/"))
            {
                var blobPath = string.Concat(source.Uri.Segments.Skip(2).Select(Uri.UnescapeDataString));
                var stream = await _blobStorageProvider.DownloadAsync("chat-images", blobPath, cancellationToken);
                if (stream != null)
                {
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                    imageBytes = memoryStream.ToArray();
                }
            }

            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            // source.MimeType is null on the path that actually matters here: LoadChatAsync
            // reconstructs ImageContent from the stored blob JSON with only a Uri, never a MimeType.
            // Defaulting that to "image/png" would mislabel every non-PNG image (jpeg, webp, gif) sent
            // to the model. Sniff it from the real bytes instead.
            var mimeType = source.MimeType ?? ImageMimeTypeHelper.DetectFromBytes(imageBytes).MimeType;

            return new ImageContent(imageBytes, mimeType);
        }

        // Metadata survives a blob JSON round-trip as System.Text.Json.JsonElement rather than its
        // original CLR type — same tolerant-cast pattern already used by
        // TenantAgentChatMessageResult.GetTimestampFromMetadata / PublicAgentChatMessageResult's
        // equivalent.
        private static DateTimeOffset? GetMessageTimestamp(ChatMessageContent message)
        {
            if (message.Metadata == null || !message.Metadata.TryGetValue(CommonMetadataConsts.Timestamp, out var value) || value == null)
                return null;

            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt),
                System.Text.Json.JsonElement element when DateTimeOffset.TryParse(element.ToString(), out var parsed) => parsed,
                string s when DateTimeOffset.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        private static ChannelMessageType GetChannelMessageType(ChatMessageContent message)
        {
            if (message.Metadata == null || !message.Metadata.TryGetValue(CommonMetadataConsts.ChannelMessageType, out var value) || value == null)
                return ChannelMessageType.Normal;

            return value switch
            {
                ChannelMessageType cmt => cmt,
                System.Text.Json.JsonElement element when Enum.TryParse<ChannelMessageType>(element.ToString(), out var parsed) => parsed,
                string s when Enum.TryParse<ChannelMessageType>(s, out var parsed) => parsed,
                int i => (ChannelMessageType)i,
                _ => ChannelMessageType.Normal
            };
        }
    }
}
