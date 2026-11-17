using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Text;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.AgentEventPublisher
{
    /// <summary>
    /// Attached to a <c>RunnableAgent</c>'s kernel when <c>RunnableAgent.EnableSpinUpAgents</c> is set
    /// (story #6480) — lets that agent's own LLM hand a message off to one or more other agents,
    /// asynchronously, without waiting for a reply. There is no <c>Tool</c>/<c>Tool.Config</c> behind
    /// this: the target agents are decided by the calling LLM at call time, by name — the LLM has no
    /// natural way to know a numeric agent id, so each target name is resolved to a real
    /// <c>RunnableAgent</c> id via a tenant-scoped lookup before publishing.
    ///
    /// This is a fire-and-forget notification — no lead/fan-out pipeline, no aggregation of results.
    /// Each target's reaction (if any) is posted into the same chat session later via the existing
    /// blob-based chat history path; this method's return value is only an acknowledgement summarizing
    /// which targets were notified successfully.
    ///
    /// US 7501: nothing is published once the session has been stopped (one read of the stop state before
    /// publishing, see <see cref="IChatStopCheckpoint"/>).
    /// </summary>
    public sealed class AgentEventPublisherPlugin
    {
        private readonly IWkfloMediator _mediator;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly TenantContext _tenantContext;
        private readonly IMasterDataDbContext _db;
        private readonly ILogger _logger;
        private readonly int _callingAgentId;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly bool? _rootIsWidget;
        private readonly bool _handOffApprovalsAvailable;
        private readonly IChatStopCheckpoint _chatStopCheckpoint;

        public AgentEventPublisherPlugin(IWkfloMediator mediator, IChatNotificationService notificationService,
            ChatContext chatContext, TenantContext tenantContext, IMasterDataDbContext db, ILogger logger, int callingAgentId,
            IBlobStorageProvider blobStorageProvider, bool? rootIsWidget = null, bool handOffApprovalsAvailable = false,
            IChatStopCheckpoint chatStopCheckpoint = null)
        {
            _chatStopCheckpoint = chatStopCheckpoint;
            _handOffApprovalsAvailable = handOffApprovalsAvailable;
            _mediator = mediator;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _tenantContext = tenantContext;
            _db = db;
            _logger = logger;
            _callingAgentId = callingAgentId;
            _blobStorageProvider = blobStorageProvider;
            _rootIsWidget = rootIsWidget;
        }

        [KernelFunction("publish_agent_event")]
        [Description("Notifies one or more other agents asynchronously, by name, without waiting for a reply — e.g. escalating or routing a request to one or more specialist agents. This only sends the notification(s); it does not wait for or return the target agents' reactions, which will appear later in this same conversation. Call this tool whenever you decide to delegate a request to another agent, whether or not the user typed @ in their message; never announce a hand-off without calling it.")]
        public async Task<string> PublishAgentEventAsync(
            [Description("JSON array of one or more target agents, each shaped {\"agentName\": <string>} — the exact name of the target agent")] string targetAgentsJson,
            [Description("The message the target agent(s) should react to")] string message,
            [Description("Free-form category describing why this event was published, e.g. 'refund-requested'")] string eventType,
            [Description("Optional. JSON array of files already present in this conversation to forward to the target agent(s), each shaped {\"fileName\": <string>, \"fileMimeType\": <string>} — both must match exactly what was shown earlier in this conversation when the file was uploaded/generated. Omit if no attachments should be forwarded.")] string fileNamesJson = null,
            CancellationToken cancellationToken = default)
        {
            // Bug 8007: lets the run detect a turn that announced a hand-off in text without making this call.
            _chatContext.AgentEventPublishCalledThisTurn = true;

            List<AgentEventTarget> targets;
            try
            {
                targets = JsonConvert.DeserializeObject<List<AgentEventTarget>>(targetAgentsJson) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentEventPublisherPlugin: invalid targetAgentsJson for session {SessionId}", _chatContext.SessionId);
                return "Failed to publish the agent event: the target list was not valid JSON.";
            }

            if (targets.Count == 0)
            {
                return "Failed to publish the agent event: no target agents were provided.";
            }

            List<AgentEventFileReference> fileRefs;
            try
            {
                fileRefs = string.IsNullOrWhiteSpace(fileNamesJson)
                    ? []
                    : JsonConvert.DeserializeObject<List<AgentEventFileReference>>(fileNamesJson) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentEventPublisherPlugin: invalid fileNamesJson for session {SessionId}", _chatContext.SessionId);
                return "Failed to publish the agent event: the file list was not valid JSON.";
            }

            var filesNotFound = new List<string>();
            var resolvedFiles = await ResolveFilesAsync(fileRefs, filesNotFound, cancellationToken);

            // Prefer the value already resolved upstream for this calling agent's own turn (e.g. a
            // Public agent invoked via ShareId — see PublicAISyncService) so every target in this call
            // shares the exact same root context. Only recompute via the AgentId heuristic when nothing
            // was passed down (e.g. a Tenant agent invoked directly, with no deterministic root value).
            var rootIsWidget = _rootIsWidget ?? await AgentChatIconHelper.GetIsWidgetContextAsync(_db, _callingAgentId, cancellationToken);

            var succeeded = new List<string>();
            var failed = new List<string>();

            // US 7501: single read of the stop state right before publishing. When the session was stopped, this
            // turn is cancelled and nothing is handed off.
            if (_chatStopCheckpoint != null)
            {
                await _chatStopCheckpoint.ThrowIfStoppedAsync(_tenantContext.TenantName, _chatContext.SessionId, ChatStopCheckpoints.HandOffPublish, cancellationToken);
            }

            foreach (var target in targets)
            {
                // US 7501: a Stop broadcast received meanwhile (no extra read) also stops the remaining hand-offs.
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(target.AgentName))
                {
                    failed.Add("(missing agent name)");
                    continue;
                }

                RunnableAgent[] matches;
                try
                {
                    // Query the root Agents DbSet, not .OfType<RunnableAgent>() — RunnableAgent is an
                    // abstract intermediate TPH type with no entity mapping of its own, which caused a
                    // real EF Core failure earlier in this story when combined with navigation-heavy
                    // queries (see RunnableAISyncService.cs). Filter/cast to RunnableAgent in memory
                    // instead, matching that already-verified pattern.
                    matches = (await _db.Agents
                        .Where(a => !a.IsDeleted && a.Tenant.Name == _tenantContext.TenantName && a.Name == target.AgentName)
                        .AsNoTracking()
                        .ToArrayAsync())
                        .OfType<RunnableAgent>()
                        .Where(a => a.IsActive)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AgentEventPublisherPlugin: agent name lookup failed for '{AgentName}', session {SessionId}", target.AgentName, _chatContext.SessionId);
                    failed.Add(target.AgentName);
                    continue;
                }

                if (matches.Length == 0)
                {
                    failed.Add($"{target.AgentName} (not found)");
                    continue;
                }

                var allowedSpinUpAgentIds = await _db.AgentAllowedSpinUpAgents
                    .Where(m => m.AgentId == _callingAgentId).Select(m => m.AllowedSpinUpAgentId)
                    .ToListAsync();

                var matchAllowedAgents = matches.Where(a => allowedSpinUpAgentIds.Contains(a.Id)).ToList();

                if (matchAllowedAgents.Count == 0)
                {
                    failed.Add($"{target.AgentName} (not found)");
                    continue;
                }

                if (matchAllowedAgents.Count > 1)
                {
                    failed.Add($"{target.AgentName} (ambiguous — more than one agent has this name)");
                    continue;
                }

                var resolvedAgent = matchAllowedAgents[0];

                try
                {
                    // US 7501: queued as this turn's hand-off (its start time), with this turn's Stop token, so a Stop that lands
                    // after the checkpoint above still drops it.
                    await _mediator.Publish(new PublishAgentEventDomainEvent(
                        resolvedAgent.Id, resolvedAgent.Name, message, _chatContext.SessionId, eventType, resolvedFiles, rootIsWidget,
                        // Bug 7722: the person chatting here can also answer the target agent's approval requests.
                        approvalRootAgentId: _handOffApprovalsAvailable ? _callingAgentId : null,
                        requestedByTurnStartedAt: _chatContext.TurnStartedAt), cancellationToken);

                    succeeded.Add(resolvedAgent.Name);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The turn was stopped while handing off: the Stop ends the turn (and drops whatever was queued).
                    _logger.LogInformation("AgentEventPublisherPlugin: hand-off to agent {AgentId} in session {SessionId} not published, the turn was stopped",
                        resolvedAgent.Id, _chatContext.SessionId);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "AgentEventPublisherPlugin failed to publish event to agent {AgentId} for session {SessionId}",
                        resolvedAgent.Id, _chatContext.SessionId);

                    failed.Add(resolvedAgent.Name);
                    continue;
                }

                // "started" fires here, immediately, because the actual invocation is fire-and-forget
                // over Dapr pub/sub (AgentEventSubscriber -> AgentActor -> AgentEventWorkflowActivity —
                // see PublishAgentEventCommandHandler) and may not run for a moment; the matching
                // "completed"/"failed" update is sent from AgentEventWorkflowActivity once that
                // eventual invocation actually finishes. Isolated in its own try/catch — same as the
                // other two activity-update call sites in this feature — so a notification failure
                // (e.g. a transient icon-lookup error) can never flip an already-published hand-off
                // into `failed`.
                try
                {
                    var chatbotIconUrl = await AgentChatIconHelper.GetChatbotIconUrlAsync(_db, resolvedAgent.Id, rootIsWidget, cancellationToken);
                    await _notificationService.SendSubagentActivityUpdate(
                        _chatContext.SessionId, resolvedAgent.Id, resolvedAgent.Name, SubagentActivityStatus.Started, chatbotIconUrl, action: eventType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "AgentEventPublisherPlugin failed to send subagent activity update for agent {AgentId} in session {SessionId}",
                        resolvedAgent.Id, _chatContext.SessionId);
                }
            }

            if (failed.Count > 0 || filesNotFound.Count > 0)
            {
                var failureParts = new List<string>();
                if (failed.Count > 0) failureParts.Add($"Failed to notify: {string.Join(", ", failed)}.");
                if (filesNotFound.Count > 0) failureParts.Add($"Files not found: {string.Join(", ", filesNotFound)}.");

                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId, "AgentEventPublisher", "failed",
                    string.Join(" ", failureParts), statusCode: 500);
            }

            if (succeeded.Count == 0)
            {
                return $"Failed to publish the agent event. Could not notify: {string.Join(", ", failed)}.";
            }

            var summary = $"Event published to: {string.Join(", ", succeeded)}.";
            if (failed.Count > 0)
            {
                summary += $" Failed to notify: {string.Join(", ", failed)}.";
            }
            if (filesNotFound.Count > 0)
            {
                summary += $" Files not found: {string.Join(", ", filesNotFound)}.";
            }
            summary += " The target agents' reactions will appear in this conversation shortly — do not claim to already have their answers.";

            return summary;
        }

        private async Task<List<FileAttachment>> ResolveFilesAsync(List<AgentEventFileReference> fileRefs, List<string> filesNotFound, CancellationToken cancellationToken)
        {
            var resolved = new List<FileAttachment>();

            if (fileRefs == null || fileRefs.Count == 0 || _chatContext.CurrentChatHistory == null)
            {
                return resolved;
            }

            foreach (var fileRef in fileRefs)
            {
                if (string.IsNullOrWhiteSpace(fileRef.FileName) || string.IsNullOrWhiteSpace(fileRef.FileMimeType))
                {
                    filesNotFound.Add(fileRef.FileName ?? "(missing file name)");
                    continue;
                }

                FileAttachment attachment;
                try
                {
                    attachment = await ResolveImageAttachmentAsync(fileRef, cancellationToken)
                        ?? ResolveDocumentAttachment(fileRef);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AgentEventPublisherPlugin: failed to resolve file '{FileName}' for session {SessionId}", fileRef.FileName, _chatContext.SessionId);
                    attachment = null;
                }

                if (attachment != null)
                {
                    resolved.Add(attachment);
                }
                else
                {
                    filesNotFound.Add(fileRef.FileName);
                }
            }

            return resolved;
        }

        // Same lookup convention as PostPlugin.UpdatePostImageByImage/PagePlugin: match on the
        // message-level FileName/FileType metadata that SemanticKernelAgentServiceRun stamps onto every
        // uploaded image message.
        private async Task<FileAttachment> ResolveImageAttachmentAsync(AgentEventFileReference fileRef, CancellationToken cancellationToken)
        {
            var imageContent = _chatContext.CurrentChatHistory
                .Where(m =>
                    m.Items.OfType<ImageContent>().Any() &&
                    m.Metadata != null &&
                    m.Metadata.ContainsKey(CommonMetadataConsts.FileName) && m.Metadata[CommonMetadataConsts.FileName].ToString() == fileRef.FileName &&
                    m.Metadata.ContainsKey(CommonMetadataConsts.FileType) && m.Metadata[CommonMetadataConsts.FileType].ToString() == fileRef.FileMimeType)
                .SelectMany(m => m.Items.OfType<ImageContent>())
                .LastOrDefault();

            if (imageContent == null)
            {
                return null;
            }

            byte[] imageBytes = null;

            if (imageContent.Data.HasValue && imageContent.Data.Value.Length > 0)
            {
                imageBytes = imageContent.Data.Value.ToArray();
            }
            else if (imageContent.Uri != null && imageContent.Uri.AbsolutePath.Contains("/chat-images/"))
            {
                var blobPath = string.Concat(imageContent.Uri.Segments.Skip(2).Select(Uri.UnescapeDataString));
                var stream = await _blobStorageProvider.DownloadAsync("chat-images", blobPath, cancellationToken);
                if (stream != null)
                {
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                    imageBytes = memoryStream.ToArray();
                }
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                return null;
            }

            return new FileAttachment
            {
                FileName = fileRef.FileName,
                FileData = $"data:{fileRef.FileMimeType};base64,{Convert.ToBase64String(imageBytes)}",
                FileSize = imageBytes.Length,
                BlobUrl = imageContent.Uri?.ToString()
            };
        }

        // Documents never get their original bytes persisted anywhere (ProcessFilesAsync only uploads
        // images to blob storage) — only the extracted text ends up in chat history, on a TextContent
        // item's own metadata rather than the message-level metadata images use. Re-encode that text as
        // a text/plain attachment; Base64Extensions.ParseDocumentData round-trips text/plain via a plain
        // UTF8 decode, so the target agent still sees the same extracted content.
        //
        // Bug 8189 / Task 8219: a long document is not in the history as text: it is a short message and its chunks are in the chat
        // document store. That one is forwarded as a reference (FileAttachment.SourceDocumentId); the target agent gets its own copy of
        // the chunks, within the same tenant, session and user. Short documents (and messages saved before this) keep the text path below.
        private FileAttachment ResolveDocumentAttachment(AgentEventFileReference fileRef)
        {
            var indexed = AttachedDocumentMessage.ReadDocuments(_chatContext.CurrentChatHistory)
                .LastOrDefault(d => d.IndexStatus != ChatDocumentIndexStatus.Failed && d.IndexedChunkCount > 0
                    && d.FileName == fileRef.FileName && d.FileType == fileRef.FileMimeType);
            if (indexed != null)
            {
                return new FileAttachment
                {
                    FileName = fileRef.FileName,
                    FileSize = indexed.FileSize,
                    SourceDocumentId = indexed.DocumentId,
                    SourceDocument = indexed
                };
            }

            const string DocumentContentPrefix = "Document content:\n";

            var textContent = _chatContext.CurrentChatHistory
                .SelectMany(m => m.Items.OfType<TextContent>())
                .LastOrDefault(t =>
                    t.Metadata != null &&
                    t.Metadata.ContainsKey(CommonMetadataConsts.FileName) && t.Metadata[CommonMetadataConsts.FileName].ToString() == fileRef.FileName &&
                    t.Metadata.ContainsKey(CommonMetadataConsts.FileType) && t.Metadata[CommonMetadataConsts.FileType].ToString() == fileRef.FileMimeType);

            if (textContent == null)
            {
                return null;
            }

            var extractedText = textContent.Text ?? string.Empty;
            if (extractedText.StartsWith(DocumentContentPrefix))
            {
                extractedText = extractedText[DocumentContentPrefix.Length..];
            }

            var textBytes = Encoding.UTF8.GetBytes(extractedText);

            return new FileAttachment
            {
                FileName = fileRef.FileName,
                FileData = $"data:text/plain;base64,{Convert.ToBase64String(textBytes)}",
                FileSize = textBytes.Length
            };
        }
    }
}
