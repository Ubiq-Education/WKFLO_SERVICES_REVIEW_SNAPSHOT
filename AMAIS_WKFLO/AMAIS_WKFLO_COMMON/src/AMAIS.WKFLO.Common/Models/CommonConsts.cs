namespace AMAIS.WKFLO.Common.Models
{
    public static class CommonConsts
    {
        public const string TenantName = "amais-wkflo-tenant-name";
        public const string TenantGuid = "amais-wkflo-tenant-guid";
        /// <summary>Acting user's email for service-to-service callers whose app-only token carries no user
        /// claims (e.g. PetraCMS with a Managed Identity). Only honoured for app-only tokens - see
        /// <c>UserContextResolver</c>.</summary>
        public const string UserEmail = "amais-wkflo-user-email";
        public const string ApiKey = "amais-wkflo-api-key";
        public const string Domain = "amais-wkflo-domain";
        public const string SessionId = "amais-wkflo-session-id";
        public const string PagePath = "amais-wkflo-page-path";
        /// <summary>CMS ID of the page the user is on, sent alongside <see cref="PagePath"/>.</summary>
        public const string PageId = "amais-wkflo-page-id";
        public const string SubjectId = "amais-wkflo-subject-id";
        public const string FeatureId = "amais-wkflo-feature-id";
        public const string FeatureType = "amais-wkflo-feature-type";
        public const string FeatureName = "amais-wkflo-feature-name";
        public const string AmaisAvailableEndpoints = "amais-wkflo-amais-endpoints";
        public const string IsTestChat = "amais-wkflo-is-test-chat";
        public const string TimeZone = "amais-wkflo-time-zone";

        public static class CommonMetadataConsts
        {
            public const string ResultType = "resultType";
            public const string Data = "data";
            public const string FormType = "formType";

            public const string IsError = "isError";
            public const string Error = "error";
            public const string MessageForAssistant = "messageForAssistant";

            public const string Id = "id";
            public const string Name = "name";
            public const string Description = "description";
            public const string Title = "title";
            public const string Tags = "tags";
            public const string FileName = "filename";
            public const string FileType = "filetype";
            public const string FileSize = "filesize";
            public const string Timestamp = "timestamp";
            public const string MessageId = "messageId";
            public const string Message = "message";
            public const string Url = "url";
            public const string ChannelMessageType = "channelMessageType";

            // Set on ChatMessageContent whose Role had to move from AuthorRole.System to
            // AuthorRole.Assistant (Anthropic rejects a 'system' role message unless it's the first
            // entry or immediately follows a user/assistant turn - see BaseAmaisPlugin and
            // SemanticKernelAgentServiceRun for the affected sources). The chat UI checks this flag
            // (OR'd with type === 'system') to keep rendering these as it did before the role change.
            public const string RenderAsSystem = "renderAsSystem";

            public const string LastUpdatedDate = "lastUpdatedDate";

            public const string Rating = "rating";
            public const string ThumbsUp = "thumbsUp";
            public const string ThumbsDown = "thumbsDown";

            public const string TopicName = "topicName";

            /// <summary>
            /// US 7078: marks a <c>ChatMessageContent</c> as ephemeral sibling-agent context injected
            /// only for this turn's model call (see LoadSiblingAgentContextBySessionQueryHandler). Any
            /// message carrying this key must be filtered out before <c>SaveChatAsync</c> — it must
            /// never reach the persisted blob.
            /// </summary>
            public const string IsEphemeralAgentContext = "isEphemeralAgentContext";

            /// <summary>
            /// US 7142 corner case: marks a persisted Assistant <c>ChatMessageContent</c> as the
            /// ContactForm tool's registration-required filler reply, NOT a real answer to the user's
            /// question — set on the assistant message whenever
            /// <c>ChatContext.ContactFormRequestedThisTurn</c> was true for that turn (see
            /// <c>SemanticKernelAgentServiceRun.RunChatCompletionAgentAsync</c>). Unlike
            /// <see cref="IsEphemeralAgentContext"/>, this DOES reach the persisted blob — it must, since
            /// <c>ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer</c> reads it back later
            /// (potentially long after the originating request/ChatContext is gone) to skip past this
            /// filler and find the real answer instead, so the backfill's own resolution judgment isn't
            /// computed from filler text either — the same failure mode the near-immediate
            /// <c>MessageResolutionWorkflow</c> path is guarded against via <c>ContactFormRequestedThisTurn</c>.
            /// </summary>
            public const string IsContactFormFiller = "isContactFormFiller";

            /// <summary>
            /// WI 7575: marks a persisted Assistant <c>ChatMessageContent</c> as the short "needs your approval"
            /// notice of a turn that stopped to wait for a tool approval - not a real answer, so it is skipped when
            /// looking for a question's answer (same treatment as <see cref="IsContactFormFiller"/>).
            /// </summary>
            public const string IsToolApprovalNotice = "isToolApprovalNotice";

            /// <summary>
            /// US 7502: marks the "Stopped. All agents cancelled." divider a turn stopped by the user saves at the end of
            /// its history. Rendered as a divider, never sent to the model as context, never taken as an answer.
            /// </summary>
            public const string IsStoppedMarker = "isStoppedMarker";

            /// <summary>
            /// US 7502: on a <see cref="IsStoppedMarker"/> message, when the user pressed Stop (ISO 8601). Every agent
            /// stopped by the same Stop writes the same value, so the merged history shows one divider per Stop.
            /// </summary>
            public const string StoppedAt = "stoppedAt";

            // Bug 8189 / Task 8219: on the text item of the short message that stands in for a large document (its full
            // text lives in the chat document store, see AttachedDocumentMessage).

            /// <summary>The document's id in the chat document store (a Guid).</summary>
            public const string DocumentId = "documentId";

            /// <summary>The document's extracted text length, in characters.</summary>
            public const string DocumentCharCount = "documentCharCount";

            /// <summary>How many chunks the document's text splits into.</summary>
            public const string DocumentChunkCount = "documentChunkCount";

            /// <summary>How many of those chunks are stored (fewer than <see cref="DocumentChunkCount"/> for a partial document).</summary>
            public const string DocumentIndexedChunkCount = "documentIndexedChunkCount";

            /// <summary>"ready", "partial" or "failed".</summary>
            public const string DocumentIndexStatus = "documentIndexStatus";

            // US 7959: live failed-turn marker (FailedTurnMarker). Sent only over SignalR, never persisted. The
            // marker also carries IsError and RenderAsSystem.

            /// <summary>US 7959: the chat runtime code (WKFLOAI11xx) of a failed turn or of a failed tool/sub-agent.</summary>
            public const string ErrorCode = "errorCode";

            /// <summary>US 7959: optional, already-public validation code behind <see cref="ErrorCode"/> (allowlist only).</summary>
            public const string DetailCode = "detailCode";

            /// <summary>US 7959: "error" (the turn failed) or "warning" (e.g. a tool is unavailable, the turn goes on).</summary>
            public const string Severity = "severity";

            /// <summary>US 7959: true when the turn ended because of this failure (the UI returns to idle).</summary>
            public const string IsTerminal = "isTerminal";

            /// <summary>US 7959: the turn the failure belongs to, so the UI shows one message per turn.</summary>
            public const string TurnId = "turnId";

            /// <summary>US 7959: when the failure was reported (ISO 8601, UTC).</summary>
            public const string FailedAt = "failedAt";

            /// <summary>US 7959: the agent that failed, when known (the same values the message envelope carries).</summary>
            public const string AgentId = "agentId";
            public const string AgentName = "agentName";
            public const string ChatbotIconUrl = "chatbotIconUrl";
        }

        public static class CommonPostMetadataConsts
        {
            public const string NewPostCreatedName = "newPostCreatedName";
            public const string NewPostCreatedAlias = "newPostCreatedAlias";
            public const string NewPostClonedName = "newPostClonedName";
            public const string NewPostClonedAlias = "newPostClonedAlias";

            public const string PostName = "postName";
            public const string MenuName = "menuName";
            public const string Alias = "alias";
            public const string MetaTitle = "metaTitle";
            public const string MetadaDescription = "metadaDescription";
            public const string OGMetaTitle = "ogMetaTitle";
            public const string OGMetaDescription = "ogMetaDescription";

            public const string RenditionId = "renditionId";
            public const string PostImageName = "postImageName";
            public const string PostImagePath = "postImagePath";
            public const string Images = "images";
        }

        public static class CommonPageMetadataConsts
        {
            public const string NewPageCreatedName = "newPageCreatedName";
            public const string NewPageCreatedAlias = "newPageCreatedAlias";
            public const string NewPageClonedName = "newPageClonedName";
            public const string NewPageClonedAlias = "newPageClonedAlias";

            public const string Alias = "alias";
            public const string PageName = "PageName";
            public const string PageDescription = "pageDescription";
            public const string PageTags = "pageTags";
            public const string PageFeatures = "pageFeatures";
            public const string MenuName = "menuName";
            public const string MetaTitle = "metaTitle";
            public const string MetadaDescription = "metadaDescription";
            public const string OGMetaTitle = "ogMetaTitle";
            public const string OGMetaDescription = "ogMetaDescription";

            public const string RenditionId = "renditionId";
            public const string PageImageName = "pageImageName";
            public const string PageImagePath = "pageImagePath";
            public const string Images = "images";
        }

        public static class CommonFeatureMetadataConsts
        {
            public const string Status = "status";
            public const string TextSaved = "textSaved";
            public const string TextPublished = "textPublished";
            public const string PublishedDate = "publishedDate";

            public const string TextFeatureType = "Text";
            public const string GalleryFeatureType = "Gallery";
        }

        public static class CommonGalleryFeatureMetadataConsts
        {
            public const string GalleryId = "galleryId";
            public const string Order = "order";
            public const string AdditionalContent = "additionalContent";
            public const string Link = "link";

            public const string RenditionId = "renditionId";
            public const string Height = "height";
            public const string Width = "width";
            public const string PresetID = "presetId";
            public const string GalleryFeatureRenditionName = "galleryFeatureRenditionName";
            public const string GalleryFeatureRenditionPath = "galleryFeatureRenditionPath";
            public const string Galleries = "galleries";
            public const string Renditions = "renditions";
            public const string GalleryImages = "galleryImages";
            public const string NotFoundRenditionIds = "notFoundRenditionIds";
        }

        public static class CommonJobTypeConsts
        {
            public const string TopicClassification = "TopicClassification";
        }

        public static class CommonJobPubSubConsts
        {
            public const string PubSubName = "pubsub-worker";
            public const string JobQueuedTopic = "wkflo-classification-job-queued";
        }

        public static class CommonAiPubSubConsts
        {
            public const string PubSubName = "pubsub-ai";
            public const string StateStoreName = "statestore-ai";
            public const string MessageKeyPrefix = "msg:";

            /// <summary>
            /// Queued messages are persisted via DaprClient.SaveStateAsync, which is bound by the
            /// daprd sidecar's --max-body-size (20M in docker-compose.override.yml for amais-wkflo-ai-webapi).
            /// Kept below that limit to leave headroom for Dapr's own envelope/serialization overhead.
            /// </summary>
            public const int MaxQueueMessageSizeBytes = 18_000_000;

            /// <summary>
            /// Grpc.Net.Client's DaprClient defaults MaxReceiveMessageSize to 4MB regardless of the
            /// daprd sidecar's own --max-body-size — that only bounds what the sidecar itself
            /// accepts/sends, not what the .NET gRPC channel will accept back. BaseWorkflowActivity's
            /// GetStateAsync (reading a queued message with image attachments back out of
            /// statestore-ai) needs this raised via DaprClientBuilder.UseGrpcChannelOptions in
            /// AddDaprClient, kept above both MaxQueueMessageSizeBytes and the sidecar's --max-body-size
            /// so this isn't the next bottleneck.
            /// </summary>
            public const int MaxGrpcMessageSizeBytes = 25 * 1024 * 1024;
        }

        /// <summary>
        /// US 7501: the Stop broadcast. A separate pub/sub component whose consumer id is unique per replica, so every
        /// AI API replica gets every message (<c>pubsub-ai</c> has a fixed consumer id: one replica per message).
        /// </summary>
        public static class CommonAiBroadcastPubSubConsts
        {
            public const string PubSubName = "pubsub-ai-broadcast";
            public const string ChatStopTopic = "wkflo-chat-stop";
        }

        public static class CommonImageAttachmentConsts
        {
            /// <summary>
            /// Anthropic's Messages API hard-rejects any single image over 10 MB decoded
            /// ("image exceeds 10 MB maximum") — a fixed, non-configurable server-side limit that
            /// applies to every Claude model, independent of the (much larger) Dapr/gRPC size limits.
            /// This constant is intentionally set well below that cutoff (5 MB) as a stricter,
            /// deliberate business limit — not just a safety margin against Anthropic's own cap.
            /// Checked before queuing so an oversized image fails fast with a clear message instead of
            /// silently dying inside a background workflow once it reaches the model call.
            /// </summary>
            public const long MaxSingleImageSizeBytes = 5_000_000;
        }

        public static class CommonToolTypeConsts
        {
            public const string Knowledge = "Knowledge";
            public const string MCP = "MCP";
            public const string IntegrationAmais = "Integration_Amais";
            public const string PriorityQA = "Priority_QA";
            public const string ChatClassification = "Chat_Classification";
            public const string ContactForm = "Contact_Form";
            public const string Http = "HTTP";
            public const string ImageGeneration = "Image_Generation";
        }

        public static class TaggableEntityTypes
        {
            public const string Agent = "Agent";
            public const string Tool = "Tool";
            public const string AIModel = "AIModel";
        }

        public static class FeatureTagConsts
        {
            public const string Maisy = "Maisy";
        }

        /// <summary>Values for <c>AuditLogEntry.Action</c>. A soft-delete (an <c>IsDeleted</c> flip on an
        /// <c>Modified</c> entry) is classified as <see cref="Delete"/>, not <see cref="Update"/> — see
        /// <c>MasterDataDbContext.BuildAuditLogEntries</c>.</summary>
        public static class AuditActionConsts
        {
            public const string Create = "Create";
            public const string Update = "Update";
            public const string Delete = "Delete";
        }

        public static class HubReceiverMethods
        {
            public const string ReceiveMessage = "ReceiveMessage";
            public const string ToolExecutionUpdate = "ToolExecutionUpdate";
            public const string SubagentActivityUpdate = "SubagentActivityUpdate";
            public const string TypingIndicator = "TypingIndicator";
            public const string StreamingTokens = "StreamingTokens";
            public const string ConversationStateUpdate = "ConversationStateUpdate";
            public const string MetadataUpdate = "MetadataUpdate";
            public const string StreamingAudio = "StreamingAudio"; //ai_audio_streaming
            public const string ContactFormRequest = "ContactFormRequest";
            public const string ContactFormCompleted = "ContactFormCompleted";
            public const string ToolApprovalRequest = "ToolApprovalRequest"; // WI 7575
            public const string ToolApprovalResolved = "ToolApprovalResolved"; // WI 7575
            public const string ConversationStopped = "ConversationStopped"; // US 7501
            public const string ConversationTitleUpdated = "ConversationTitleUpdated"; // US 7507
        }

        public static class CommonErrorConst
        {
            public const string UnexpectedResult = "WKFLO0001";
            public const string UnauthorizedResult = "WKFLO0002";
            public const string NotFound = "WKFLO0003";

            public const string InvalidInput = "WKFLO1001";
            public const string Required = "WKFLO1002";

            public const string NoClientId = "WKFLO3001";

            public const string NoTenantId = "WKFLO4001";

            public const string NoDomainId = "WKFLO5001";
        }

        public static class AmaisJwtConst
        {
            public const string SchemeName = "AmaisJwtScheme";
        }

        /// <summary>
        /// Values for the "Pool" field reported to PUM in <c>UsageLogDataSample.Metadata</c> (WI 7365 —
        /// see <c>PumLogHelper.ReportUsageAsync</c>). PetraCMS logs its own Chatbot feature directly and
        /// tags it "Chatbot"; a tenant can also tag a wkflo_services agent "Chatbot" (e.g. a WKFlo agent
        /// standing in for/embedded as the site chatbot), in which case its usage should count toward the
        /// same pool rather than the default MAISY-and-sub-agents one. Everything else reports
        /// <see cref="MaisyAndSubagents"/>.
        /// </summary>
        public static class PumPoolConsts
        {
            public const string Chatbot = "Chatbot";
            public const string MaisyAndSubagents = "MaisyAndSubagents";
        }
    }
}
