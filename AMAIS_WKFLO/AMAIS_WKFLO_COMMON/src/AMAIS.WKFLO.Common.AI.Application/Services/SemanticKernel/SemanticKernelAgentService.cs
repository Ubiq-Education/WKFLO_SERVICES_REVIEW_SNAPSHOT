using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.PageAnalytics;
using AMAIS.WKFLO.Common.AI.Application.Plugins.AttachedDocuments;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Documents;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.Common.Models.SignalR;
using EFCoreSecondLevelCacheInterceptor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Text;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService : ISemanticKernelAgentService
    {
        private readonly ILogger<SemanticKernelAgentService> _logger;
        // The host's logger factory, handed to every kernel so its filters and plugins log to the app's sinks.
        private readonly ILoggerFactory _loggerFactory;
        private readonly IWkfloMapper _mapper;
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly IEisKnowledgeProvider _eisKnowledgeProvider;
        private readonly IChatNotificationService _notificationService;
        private readonly IMcpClientService _mcpService;
        private readonly IWkfloMediator _mediator;
        private readonly IOptions<ElasticsearchSettings> _elasticsearchSettings;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly IBlobStorageProvider _blobStorageProvider;
        private readonly IAzureSpeechProvider _azureSpeechService;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IMasterDataDbContext _db;
        private readonly IContactFormPluginLogService _contactFormLogService;
        private readonly AgentSharableContext _agentSharableContext;
        private readonly IMessageResolutionWorkflowScheduler _messageResolutionWorkflowScheduler;
        private readonly IContactFormReplayStateService _contactFormReplayStateService;
        private readonly IToolApprovalStateService _toolApprovalStateService;
        private readonly IToolApprovalExpiryScheduler _toolApprovalExpiryScheduler;
        private readonly IChatStopCheckpoint _chatStopCheckpoint;
        private readonly IChatStopStateStore _chatStopStateStore;
        private readonly IChatTurnFailureState _chatTurnFailureState;
        private readonly IChatTurnErrorReporter _chatTurnErrorReporter;
        private readonly IChatDocumentIndexer _chatDocumentIndexer;
        private readonly IChatDocumentRetriever _chatDocumentRetriever;
        private readonly ChatDocumentSettings _chatDocumentSettings;

        public SemanticKernelAgentService(ILogger<SemanticKernelAgentService> logger, IWkfloMapper mapper, IDatastreamProvider datastreamProvider, IEisKnowledgeProvider eisKnowledgeProvider,
            IChatNotificationService notificationService, IMcpClientService mcpService, IWkfloMediator mediator, IOptions<ElasticsearchSettings> settings,
            TenantContext tenantContext, ChatContext chatContext, UserContext userContext, IBlobStorageProvider blobStorageProvider,
            IAzureSpeechProvider azureSpeechService, IDateTimeProvider dateTimeProvider, IMasterDataDbContext db,
            IContactFormPluginLogService contactFormLogService,
            AgentSharableContext agentSharableContext = null,
            IMessageResolutionWorkflowScheduler messageResolutionWorkflowScheduler = null,
            IContactFormReplayStateService contactFormReplayStateService = null,
            IToolApprovalStateService toolApprovalStateService = null,
            IToolApprovalExpiryScheduler toolApprovalExpiryScheduler = null,
            IChatStopCheckpoint chatStopCheckpoint = null,
            IChatStopStateStore chatStopStateStore = null,
            IChatTurnFailureState chatTurnFailureState = null,
            IChatTurnErrorReporter chatTurnErrorReporter = null,
            IChatDocumentIndexer chatDocumentIndexer = null,
            IOptions<ChatDocumentSettings> chatDocumentSettings = null,
            IChatDocumentRetriever chatDocumentRetriever = null,
            ILoggerFactory loggerFactory = null)
        {
            // Bug 8189 / Task 8219: without them (e.g. some tests) every document stays inline, as before.
            _chatDocumentIndexer = chatDocumentIndexer;
            _chatDocumentRetriever = chatDocumentRetriever;
            _chatDocumentSettings = chatDocumentSettings?.Value ?? new ChatDocumentSettings();
            // US 7959 / Task 7961: failures are recorded here instead of messaging the chat; the turn's choke point
            // (BaseWorkflowActivity) reports them once. Without a scope's state (e.g. some tests) they are only logged.
            _chatTurnFailureState = chatTurnFailureState ?? new ChatTurnFailureState();
            _chatTurnErrorReporter = chatTurnErrorReporter;
            _chatStopCheckpoint = chatStopCheckpoint;
            _chatStopStateStore = chatStopStateStore;
            _toolApprovalStateService = toolApprovalStateService;
            _toolApprovalExpiryScheduler = toolApprovalExpiryScheduler;
            _logger = logger;
            _loggerFactory = loggerFactory;
            _mapper = mapper;
            _datastreamProvider = datastreamProvider;
            _eisKnowledgeProvider = eisKnowledgeProvider;
            _notificationService = notificationService;
            _mcpService = mcpService;
            _mediator = mediator;
            _elasticsearchSettings = settings;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _blobStorageProvider = blobStorageProvider;
            _azureSpeechService = azureSpeechService;
            _dateTimeProvider = dateTimeProvider;
            _db = db;
            _contactFormLogService = contactFormLogService;
            _agentSharableContext = agentSharableContext;
            _messageResolutionWorkflowScheduler = messageResolutionWorkflowScheduler;
            _contactFormReplayStateService = contactFormReplayStateService;
        }

        public async Task<AgentAnswer> RunTextAsync(RunTextOptions options, CancellationToken cancellationToken)
        {
            // US 7501/7502: the data handed to the model run, once built (it tells which phase a Stop hit and whether
            // the turn saved itself).
            RunChatData runData = null;

            // US 7959 / Task 7961: a failure while the attachments are processed is reported as an attachment problem.
            var processingAttachments = false;
            try
            {
                _chatTurnErrorReporter?.SetCurrentAgent(new ChatTurnAgent(options.Agent.Id, options.Agent.Name));

                // Get kernel for provider
                Kernel kernel = AIProviderHelper.GetKernel(options.Provider, _notificationService, _dateTimeProvider, _chatContext, _mcpService, _blobStorageProvider, _eisKnowledgeProvider, _userContext, _tenantContext, _chatTurnErrorReporter, _loggerFactory);

                // Check if kernel is null
                if (kernel == null)
                {
                    // US 7959 / Task 7961: never sent as an answer (it named an internal type); the chat gets a code.
                    await RecordUnsupportedProviderAsync(options.Agent, options.Provider?.GetType().Name, options.SessionId, options.RootIsWidget, cancellationToken);
                    return null;
                }

                // Get workflow prompt
                var prompt = await kernel.GetWkflowPromptAsync(options.Agent);

                // WI 7447/7575: effective tool permissions for this run. Ask needs someone to approve it, which
                // only an interactive chat has (see RunTextOptions.ApprovalsAvailable); otherwise it acts as Block.
                var resume = options.ToolApprovalResume;
                var toolPermissionPolicy = ToolPermissionPolicy.Build(options.Agent, ResolveApprovalsAvailable(resume != null || options.ApprovalsAvailable || _chatContext.ToolApprovalsAvailable));
                toolPermissionPolicy = PolicyForResumedTurn(toolPermissionPolicy, resume);

                // Process files (e.g. upload images to blob storage and replace file data with blob URL)
                processingAttachments = true;
                await ProcessFilesAsync(options.Files, options.SessionId, options.Agent, kernel, options.AgentTagNames, cancellationToken);
                processingAttachments = false;

                // Load chat history
                var (createdAt, chatName, chatHistoryAgentThread, detachedStoppedMarkers) = await LoadChatHistoryBySessionIdAsync(options.Agent, options.SessionId, cancellationToken, kernel, options.AgentTagNames);

                // WI 7575: approval cards still shown as Pending may have been decided or expired since.
                await SyncToolApprovalCardsAsync(chatHistoryAgentThread.ChatHistory, cancellationToken);

                // US 7974: taken before the reasoning below is streamed, see RunChatData.UserMessageTimestamp.
                var userMessageTimestamp = _dateTimeProvider.DateTimeOffsetUtcNow();

                // Run agent with or without chain of thought
                var assitantReasoning = string.Empty;
                DateTimeOffset? assitantReasoningTimestamp = null;
                var imageAnalysis = string.Empty;
                if (options.ChainOfThought && resume == null)
                {
                    if (options.Files != null && options.Files.Any())
                    {
                        imageAnalysis = await RunImageAnalyzerChatCompletionAgentAsync(new RunImageAnalyzerChatData()
                        {
                            Kernel = kernel,
                            Agent = options.Agent,
                            Message = options.Message,
                            Files = options.Files,
                            SessionId = options.SessionId
                        }, cancellationToken);
                    }

                    (assitantReasoning, var reasoningTimestamp) = await RunReasoningChatCompletionAgentAsync(new RunReasoningChatData()
                    {
                        Kernel = kernel,
                        Agent = options.Agent,
                        Prompt = prompt,
                        Message = options.Message,
                        ImageAnalysis = imageAnalysis,
                        SessionId = options.SessionId,
                        ChatHistoryAgentThread = chatHistoryAgentThread,
                        RootIsWidget = options.RootIsWidget
                    }, cancellationToken);
                    assitantReasoningTimestamp = reasoningTimestamp;
                }

                // Add workflow tools to kernel
                // Bug 7722: the person chatting can also answer approval requests of the agents this (root) agent
                // hands off to - approvals are only passed on one level, never from a sub-agent run.
                var handOffApprovalsAvailable = toolPermissionPolicy.ApprovalsAvailable && options.RootAgentId == null;

                var amaisSkillsEnabled = await kernel.AddWkflowToolsAsync(options.Agent, _datastreamProvider, _notificationService, _elasticsearchSettings, _mcpService, _tenantContext, _chatContext, _userContext, options.AgentTagNames, _mapper, _dateTimeProvider, _logger, _blobStorageProvider, _mediator, _contactFormLogService, _db, options.RootIsWidget, _agentSharableContext, toolPermissionPolicy, handOffApprovalsAvailable, _chatStopCheckpoint, _chatTurnErrorReporter);

                // Bug 8189 / Task 8219: lets the model search the chat's long documents for more than this turn's passages.
                AddAttachedDocumentsSearchTool(kernel, chatHistoryAgentThread.ChatHistory, options.Files, options.SessionId);

                if (amaisSkillsEnabled)
                {
                    // Auto-select AMAIS plugins
                    await kernel.AutoSelectAmaisPluginsAsync(chatHistoryAgentThread.ChatHistory, options.Message);

                    // Auto-invoke page data if PagePlugin is loaded and PagePath is available
                    await kernel.AutoInvokePageDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider, cancellationToken);

                    // Auto-invoke text feature data if TextFeaturePlugin is loaded and FeatureType is "Text"
                    await kernel.AutoInvokeTextFeatureDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider, cancellationToken);

                    // Auto-invoke gallery feature data if GalleryFeaturePlugin is loaded and FeatureType is "Gallery"
                    await kernel.AutoInvokeGalleryFeatureDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider, cancellationToken);
                }

                // Auto-invoke AMAIS knowledge if AmaisKnowledgePlugin is loaded
                await kernel.AutoInvokeAmaisKnowledgeAsync(_chatContext, chatHistoryAgentThread.ChatHistory, options.Message, _dateTimeProvider, cancellationToken);

                // Enforce tool call limit if configured
                if (options.Agent.MaxToolCallsPerTurn.HasValue)
                {
                    kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(options.Agent.MaxToolCallsPerTurn.Value));
                }

                // US 7501: before each tool call the model asks for, one read of the session's stop state.
                if (_chatStopCheckpoint != null)
                {
                    kernel.AutoFunctionInvocationFilters.Add(new ChatStopCheckpointFilter(_chatStopCheckpoint, _tenantContext.TenantName, options.SessionId));
                }

                // Generate user message ID shared between classification record and chat history - except
                // for a ContactForm replay with a resolved OriginalMessageId, which reuses the ORIGINAL
                // question's id instead (see RunTextOptions.OriginalMessageId's own doc comment for why:
                // this is what lets MessageResolutionWorkflow correlate the real final answer back to the
                // same MessageTopicClassification row).
                var userMessageId = options.IsContactFormReplay && options.OriginalMessageId.HasValue
                    ? options.OriginalMessageId.Value
                    : resume?.UserMessageId ?? Guid.NewGuid();

                // WI 7575: resume turn - run (or refuse) the STORED call and put its outcome in place of the
                // placeholder result; the model is then cued to continue (no model replay of the call).
                var message = options.Message;
                if (resume != null)
                {
                    message = await ApplyToolApprovalDecisionAsync(kernel, toolPermissionPolicy, chatHistoryAgentThread.ChatHistory, resume, cancellationToken);
                }

                // WI 7575: "Ask for permission" calls are caught here (only when someone can answer).
                var toolApprovalFilter = await AddToolApprovalFilterAsync(kernel, options, toolPermissionPolicy, userMessageId, cancellationToken);

                // Run chat classification if the agent has a ChatClassification tool configured.
                // Skipped entirely for a ContactForm replay turn (see RunChatClassificationAsync's own
                // isContactFormReplay guard) — the replayed text is the same question already classified
                // during the original turn. Same for a tool approval resume turn (no new question).
                var (topicName, topicHandoffMessage) = await RunChatClassificationAsync(kernel, options.Agent, message, userMessageId, options.SessionId, options.IsContactFormReplay || resume != null, cancellationToken);

                // Run agent
                runData = new RunChatData()
                {
                    Kernel = kernel,
                    Agent = options.Agent,
                    AgentTagNames = options.AgentTagNames,
                    Prompt = prompt,
                    Message = message,
                    Files = resume == null ? options.Files : [],
                    SessionId = options.SessionId,
                    ChatHistoryAgentThread = chatHistoryAgentThread,
                    CreatedAt = createdAt,
                    ChatName = chatName,
                    AssitantReasoning = assitantReasoning,
                    AssitantReasoningTimestamp = assitantReasoningTimestamp,
                    ImageAnalysis = imageAnalysis,
                    UserMessageId = userMessageId,
                    UserMessageTimestamp = userMessageTimestamp,
                    TopicName = topicName,
                    TopicHandoffMessage = topicHandoffMessage,
                    RootIsWidget = options.RootIsWidget,
                    AmaisSkillsEnabled = amaisSkillsEnabled,
                    IsMaisyAgent = options.IsMaisyAgent,
                    IsContactFormReplay = options.IsContactFormReplay,
                    ToolPermissionPolicy = toolPermissionPolicy,
                    ToolApprovalFilter = toolApprovalFilter,
                    IsToolApprovalResume = resume != null,
                    DetachedStoppedMarkers = detachedStoppedMarkers
                };
                return await RunChatCompletionAgentAsync(runData, cancellationToken);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                // US 7501: the user stopped this turn. Not an error: no error bubble, and the cancellation reaches the
                // workflow activity, which records the turn as cancelled. Some providers wrap the cancellation, so any
                // exception thrown once the token is cancelled is treated as the Stop.
                // US 7502: stopped before the answer started streaming: the history keeps only the "Stopped" marker.
                // (Stopped while streaming, the run already saved it; this then only reads its outcome.)
                var marker = await SaveEarlyStoppedTurnAsync(options, runData);
                _logger.LogInformation("Turn stopped: session {SessionId}, agent {AgentId}, phase {StopPhase}, Stopped marker {StoppedMarker}",
                    options.SessionId, options.Agent?.Id, runData?.StreamingStarted == true ? "streaming" : "before streaming", marker);

                if (ex is OperationCanceledException)
                    throw;

                throw new OperationCanceledException("The chat turn was stopped.", ex, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SemanticKernelAgentService failed");
                var chatbotIconUrl = await TryGetChatbotIconUrlAsync(options.Agent.Id, options.RootIsWidget, cancellationToken);

                // US 7959 / Task 7961: recorded (a code only, never the exception text); the turn's choke point tells the chat.
                RecordTurnFailure(ex, options.Agent, chatbotIconUrl, cancellationToken.IsCancellationRequested,
                    processingAttachments ? ChatErrorCodes.ChatAttachmentFailed : null);

                await _notificationService.SendTypingIndicator(options.SessionId, false, options.Agent.Id, options.Agent.Name, chatbotIconUrl);
                return null;
            }
        }

        public async Task<AgentAnswer> RunVoiceAsync(RunVoiceOptions options, CancellationToken cancellationToken)
        {
            try
            {
                _chatTurnErrorReporter?.SetCurrentAgent(new ChatTurnAgent(options.Agent.Id, options.Agent.Name));

                // Get kernel for provider
                Kernel kernel = AIProviderHelper.GetKernel(options.Provider, _notificationService, _dateTimeProvider, _chatContext, _mcpService, _blobStorageProvider, _eisKnowledgeProvider, _userContext, _tenantContext, _chatTurnErrorReporter, _loggerFactory);

                // Check if kernel is null
                if (kernel == null)
                {
                    // US 7959 / Task 7961: never sent as an answer (it named an internal type); the chat gets a code.
                    await RecordUnsupportedProviderAsync(options.Agent, options.Provider?.GetType().Name, options.SessionId, options.RootIsWidget, cancellationToken);
                    return null;
                }

                // Get workflow prompt
                var prompt = await kernel.GetWkflowPromptAsync(options.Agent);

                // WI 7447/7575: nobody can answer a tool approval request in a voice conversation - Ask acts as Block.
                var toolPermissionPolicy = ToolPermissionPolicy.Build(options.Agent, approvalsAvailable: false);

                // Load chat history
                var (createdAt, chatName, chatHistoryAgentThread, detachedStoppedMarkers) = await LoadChatHistoryBySessionIdAsync(options.Agent, options.SessionId, cancellationToken, kernel, options.AgentTagNames);

                // Run agent with or without chain of thought
                var assitantReasoning = string.Empty;
                if (options.ChainOfThought)
                {
                    // The voice run does not persist its reasoning, so its timestamp is not needed.
                    (assitantReasoning, _) = await RunReasoningChatCompletionAgentAsync(new RunReasoningChatData()
                    {
                        Kernel = kernel,
                        Agent = options.Agent,
                        Prompt = prompt,
                        Message = options.Message,
                        SessionId = options.SessionId,
                        ChatHistoryAgentThread = chatHistoryAgentThread,
                        RootIsWidget = options.RootIsWidget
                    }, cancellationToken);
                }

                // Add workflow tools to kernel
                var amaisSkillsEnabled = await kernel.AddWkflowToolsAsync(options.Agent, _datastreamProvider, _notificationService, _elasticsearchSettings, _mcpService, _tenantContext, _chatContext, _userContext, options.AgentTagNames, _mapper, _dateTimeProvider, _logger, _blobStorageProvider, _mediator, _contactFormLogService, _db, options.RootIsWidget, _agentSharableContext, toolPermissionPolicy, chatStopCheckpoint: _chatStopCheckpoint, chatTurnErrorReporter: _chatTurnErrorReporter);

                // Bug 8189 / Task 8219: lets the model search the chat's long documents for more than this turn's passages.
                AddAttachedDocumentsSearchTool(kernel, chatHistoryAgentThread.ChatHistory, null, options.SessionId);

                if (amaisSkillsEnabled)
                {
                    // Auto-select AMAIS plugins
                    await kernel.AutoSelectAmaisPluginsAsync(chatHistoryAgentThread.ChatHistory, options.Message);

                    // Auto-invoke page data if PagePlugin is loaded and PagePath is available
                    await kernel.AutoInvokePageDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider);

                    // Auto-invoke text feature data if TextFeaturePlugin is loaded and FeatureType is "Text"
                    await kernel.AutoInvokeTextFeatureDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider);

                    // Auto-invoke gallery feature data if GalleryFeaturePlugin is loaded and FeatureType is "Gallery"
                    await kernel.AutoInvokeGalleryFeatureDataAsync(_chatContext, chatHistoryAgentThread.ChatHistory, _dateTimeProvider);
                }

                // Auto-invoke AMAIS knowledge if AmaisKnowledgePlugin is loaded
                await kernel.AutoInvokeAmaisKnowledgeAsync(_chatContext, chatHistoryAgentThread.ChatHistory, options.Message, _dateTimeProvider);

                // Enforce tool call limit if configured
                if (options.Agent.MaxToolCallsPerTurn.HasValue)
                {
                    kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(options.Agent.MaxToolCallsPerTurn.Value));
                }

                // Generate user message ID shared between the classification record and the persisted
                // chat history - mirrors RunTextAsync's own userMessageId. Voice has no ContactForm-replay
                // concept (no upstream Voice command carries an OriginalMessageId equivalent), so this is
                // always a freshly-generated id.
                var userMessageId = Guid.NewGuid();

                // Run chat classification if the agent has a ChatClassification tool configured - same
                // call the text path already makes, so voice-triggered messages get topic classification
                // (and therefore message-level resolution judgment, US 7142) too, instead of being
                // permanently invisible to Topic Detail analytics.
                var (topicName, topicHandoffMessage) = await RunChatClassificationAsync(kernel, options.Agent, options.Message, userMessageId, options.SessionId, isContactFormReplay: false, cancellationToken);

                // Run agent
                return await RunVoiceChatCompletionAgentAsync(new RunVoiceChatData()
                {
                    Kernel = kernel,
                    Agent = options.Agent,
                    AgentTagNames = options.AgentTagNames,
                    Prompt = prompt,
                    Message = options.Message,
                    SessionId = options.SessionId,
                    ChatHistoryAgentThread = chatHistoryAgentThread,
                    CreatedAt = createdAt,
                    ChatName = chatName,
                    BranchId = options.BranchId,
                    Voice = options.VoiceName,
                    Language = options.VoiceLangCode,
                    AssitantReasoning = assitantReasoning,
                    RootIsWidget = options.RootIsWidget,
                    AmaisSkillsEnabled = amaisSkillsEnabled,
                    IsMaisyAgent = options.IsMaisyAgent,
                    UserMessageId = userMessageId,
                    TopicName = topicName,
                    TopicHandoffMessage = topicHandoffMessage,
                    ToolPermissionPolicy = toolPermissionPolicy,
                    DetachedStoppedMarkers = detachedStoppedMarkers
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SemanticKernelAgentService failed");
                var chatbotIconUrl = await TryGetChatbotIconUrlAsync(options.Agent.Id, options.RootIsWidget, cancellationToken);

                // US 7959 / Task 7961: recorded (a code only); nothing when the user stopped the turn (cancelled token).
                RecordTurnFailure(ex, options.Agent, chatbotIconUrl, cancellationToken.IsCancellationRequested);

                await _notificationService.SendTypingIndicator(options.SessionId, false, options.Agent.Id, options.Agent.Name, chatbotIconUrl);
                return null;
            }
        }


        #region Private Methods

        /// <summary>
        /// US 7959 / Task 7961: records why this turn failed - <paramref name="code"/> when given, else the classified
        /// <paramref name="exception"/> - for the turn's choke point to report once (this replaces the code-less error
        /// bubble the run used to send itself). Nothing when the user stopped the turn. The exception only goes to the logs.
        /// </summary>
        private void RecordTurnFailure(Exception exception, RunnableAgent agent, string chatbotIconUrl, bool stopRequested, string code = null)
        {
            if (stopRequested)
                return;

            code ??= ChatErrorClassifier.Classify(exception, stopRequested);
            if (code == null)
                return;

            _chatTurnFailureState.Record(ChatTurnFailure.Create(code, agent?.Id, agent?.Name, chatbotIconUrl), exception);
        }

        /// <summary>
        /// US 7959 / Task 7961: the agent's provider has no kernel. Logged with the provider type; the chat only gets
        /// <see cref="ChatErrorCodes.ChatModelConfigInvalid"/>. Its thinking bubble stops.
        /// </summary>
        private async Task RecordUnsupportedProviderAsync(RunnableAgent agent, string providerType, string sessionId, bool? rootIsWidget, CancellationToken cancellationToken)
        {
            _logger.LogError("The provider type {ProviderType} of agent {AgentId} is not supported", providerType, agent.Id);
            var chatbotIconUrl = await TryGetChatbotIconUrlAsync(agent.Id, rootIsWidget, cancellationToken);
            _chatTurnFailureState.Record(ChatTurnFailure.Create(ChatErrorCodes.ChatModelConfigInvalid, agent.Id, agent.Name, chatbotIconUrl));
            await _notificationService.SendTypingIndicator(sessionId, false, agent.Id, agent.Name, chatbotIconUrl);
        }

        internal async Task ProcessFilesAsync(List<FileAttachment> files, string sessionId, RunnableAgent agent, Kernel kernel, IReadOnlyList<string> agentTagNames,
            CancellationToken cancellationToken)
        {
            // If there are no files, return early.
            if (files == null || !files.Any()) return;

            // Upload images to blob storage before passing files to Run or Reasoning.
            // This sets file.BlobUrl so both methods use the URL instead of raw bytes,
            // preventing image bytes from entering the persistent ChatHistory JSON.
            foreach (var file in files)
            {
                // Bug 8189 / Task 8219: a document another agent already holds in this chat gets its own copy of the chunks.
                if (file.SourceDocumentId.HasValue)
                {
                    await AttachForwardedDocumentAsync(file, agent, sessionId, cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.FileData)) continue;
                var mimeType = file.FileData.DetectMimeType();

                if (mimeType.IsDocumentMimeType())
                {
                    await ProcessDocumentAsync(file, agent, kernel, agentTagNames, sessionId, cancellationToken);
                    continue;
                }

                if (!mimeType.IsImageMimeType()) continue;

                string blobName = null;
                try
                {
                    var (rawBase64, detectedMimeType) = file.FileData.ParseRawBase64Data();
                    // US 7974 / Task 7979: stored under the tenant's prefix. An unknown tenant must not fail the
                    // user's turn over a storage layout detail: it falls back to the legacy name (still purged).
                    if (!ChatImageBlobNames.TryGetTenantSegment(_tenantContext?.TenantGuid, out _))
                        _logger.LogWarning("Tenant unknown for session {SessionId}; storing uploaded image under the legacy blob name", sessionId);
                    blobName = ChatImageBlobNames.ForUserUpload(_tenantContext?.TenantGuid, sessionId, Guid.NewGuid(), file.FileName);
                    file.BlobUrl = await _blobStorageProvider.UploadBase64Async(
                        ChatImageBlobNames.ContainerName, blobName, rawBase64, detectedMimeType, cancellationToken);

                    file.BlobUrl = await _blobStorageProvider.GenerateSasUrlAsync(new Uri(file.BlobUrl), TimeSpan.FromHours(1));
                    // No file name or (SAS) URL in logs: the user's file name is in both, and the URL grants access.
                    _logger.LogInformation("Uploaded chat image to blob {BlobRef}", BlobNameLogFormatter.ForLog(blobName));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload chat image {BlobRef} to chat-images blob storage", BlobNameLogFormatter.ForLog(blobName));
                    throw;
                }
            }
        }

        /// <summary>
        /// Bug 8189 / Task 8219: extracts a document's text once. A long document is indexed (chunks, embeddings and a summary) and
        /// only its details are kept in <see cref="FileAttachment.Document"/>; a short one keeps its text in
        /// <see cref="FileAttachment.ExtractedText"/>, inline as before. Either way the file data (base64, up to tens of MB) is released.
        /// </summary>
        private async Task ProcessDocumentAsync(FileAttachment file, RunnableAgent agent, Kernel kernel, IReadOnlyList<string> agentTagNames, string sessionId,
            CancellationToken cancellationToken)
        {
            var (_, detectedMimeType, extractedText) = file.FileData.ParseDocumentData();
            file.FileType = detectedMimeType;
            file.FileData = null;

            // Whitespace only (e.g. a scanned PDF with no text layer) is treated as no text: nothing to index or show.
            if (string.IsNullOrWhiteSpace(extractedText))
                return;

            if (_chatDocumentIndexer != null && extractedText.Length > _chatDocumentSettings.InlineMaxChars)
            {
                var scope = ChatDocumentScope.For(_tenantContext.TenantName, sessionId, _userContext.Guid);
                // Known limitation: the chunks are stored before the history that references them is saved. If the turn fails
                // after this and before the save, they stay in chat_documents unreferenced (documents have no scheduled purge).
                file.Document = await _chatDocumentIndexer.IndexAsync(
                    new ChatDocumentIndexRequest(scope, agent, kernel, agentTagNames, file.FileName, detectedMimeType, file.FileSize, extractedText), cancellationToken);
                return;
            }

            file.ExtractedText = extractedText;
        }

        private async Task AttachForwardedDocumentAsync(FileAttachment file, RunnableAgent agent, string sessionId, CancellationToken cancellationToken)
        {
            file.FileType = file.SourceDocument?.FileType;

            if (_chatDocumentIndexer == null || file.SourceDocument == null)
            {
                _logger.LogWarning("A forwarded document has no source details; it is not attached");
                return;
            }

            file.SourceDocument.DocumentId = file.SourceDocumentId.Value;
            var scope = ChatDocumentScope.For(_tenantContext.TenantName, sessionId, _userContext.Guid);
            file.Document = await _chatDocumentIndexer.CopyAsync(scope, file.SourceDocument, agent.Id, cancellationToken);

            if (file.Document == null)
                _logger.LogWarning("The source document {DocumentId} of a forwarded attachment has no stored chunks; it is not attached", file.SourceDocumentId);
        }

        /// <summary>
        /// Resolves the agent's configured chat icon URL (AgentChatSetting.ChatbotIconPath).
        /// <paramref name="rootIsWidget"/> is the root agent's own widget/non-widget context when
        /// <paramref name="agentId"/> is a subagent invoked via a hand-off (see
        /// <see cref="RunTextOptions.RootIsWidget"/>); null when resolving a root agent's own icon.
        /// </summary>
        private Task<string> GetChatbotIconUrlAsync(int agentId, bool? rootIsWidget, CancellationToken cancellationToken) =>
            AgentChatIconHelper.GetChatbotIconUrlAsync(_db, agentId, rootIsWidget, cancellationToken);

        // Best-effort variant for error-recovery paths: the stop-typing notification below must still
        // fire even if the icon lookup itself fails (e.g. the DbContext is unusable after the original
        // failure, or cancellationToken is already canceled) — a lost stop notification leaves the
        // client stuck showing "typing" indefinitely, which is worse than a missing icon.
        private async Task<string> TryGetChatbotIconUrlAsync(int agentId, bool? rootIsWidget, CancellationToken cancellationToken)
        {
            try
            {
                return await GetChatbotIconUrlAsync(agentId, rootIsWidget, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SemanticKernelAgentService failed to resolve chatbot icon for agent {AgentId}", agentId);
                return null;
            }
        }

        private async Task ProcessAssistantPostMessagesAsync(ChatHistory chatHistory, RunnableAgent agent, bool? rootIsWidget)
        {
            if (_chatContext.AssistantPostMessagesToBeProcessed.Count > 0)
            {
                var chatbotIconUrl = await GetChatbotIconUrlAsync(agent.Id, rootIsWidget, default);

                foreach (var postMessageToBeProcessed in _chatContext.AssistantPostMessagesToBeProcessed)
                {
                    // New Message Id
                    var messageId = Guid.NewGuid().ToString();

                    var role = postMessageToBeProcessed.Role == AuthorRole.Assistant ? "assistant" :
                                postMessageToBeProcessed.Role == AuthorRole.User ? "user" :
                                postMessageToBeProcessed.Role == AuthorRole.System ? "system" :
                                postMessageToBeProcessed.Role == AuthorRole.Tool ? "tool" :
                                string.Empty;

                    var channelType = ChannelMessageType.Normal;
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.ChannelMessageType))
                    {
                        channelType = (ChannelMessageType)postMessageToBeProcessed.Metadata[CommonMetadataConsts.ChannelMessageType];
                    }

                    var time = _dateTimeProvider.DateTimeOffsetUtcNow();
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.Timestamp))
                    {
                        time = (DateTimeOffset)postMessageToBeProcessed.Metadata[CommonMetadataConsts.Timestamp];
                    }

                    var metadata = new Dictionary<string, object>();
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.Url))
                    {
                        metadata.Add(CommonMetadataConsts.Url, (string)postMessageToBeProcessed.Metadata[CommonMetadataConsts.Url]);
                    }
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.FormType))
                    {
                        metadata.Add(CommonMetadataConsts.FormType, (string)postMessageToBeProcessed.Metadata[CommonMetadataConsts.FormType]);
                    }
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.Data))
                    {
                        metadata.Add(CommonMetadataConsts.Data, (string)postMessageToBeProcessed.Metadata[CommonMetadataConsts.Data]);
                    }
                    if (postMessageToBeProcessed.Metadata != null && postMessageToBeProcessed.Metadata.ContainsKey(CommonMetadataConsts.RenderAsSystem))
                    {
                        metadata.Add(CommonMetadataConsts.RenderAsSystem, (bool)postMessageToBeProcessed.Metadata[CommonMetadataConsts.RenderAsSystem]);
                    }

                    // Send the response message
                    var metadataMessage = new ChatMessage
                    {
                        Id = messageId,
                        Type = postMessageToBeProcessed.Role.ToString(),
                        // Task 8215: an image's FileName/FileType label stays in the history, not in the chat.
                        Content = ChatImageLabel.ContentForChat(postMessageToBeProcessed),
                        ChannelMessageType = channelType,
                        Metadata = metadata,
                        Timestamp = time,
                        FileUrl = postMessageToBeProcessed.Items?.OfType<ImageContent>().FirstOrDefault()?.Uri?.AbsoluteUri
                    };

                    await _notificationService.SendMessageToChat(_chatContext.SessionId, metadataMessage, agent.Id, agent.Name, chatbotIconUrl);
                    chatHistory.Add(postMessageToBeProcessed);
                }

                _chatContext.AssistantPostMessagesToBeProcessed.Clear();
            }
        }

        /// <summary>
        /// Reorganizes a list of tokens by joining consecutive elements until each string is at least 20 characters long.
        /// This reduces the number of items in the list while making each one longer. 
        /// 
        /// Important Note: This is to prevent to render wrong starting Html like '<p' which the browser will render it as normal text making a weird effect on the UI.
        /// 
        /// </summary>
        /// <param name="tokens">The list of tokens to reorganize.</param>
        /// <returns>A new list where each string is at least 20 characters long (except possibly the last one).</returns>
        private List<string> ReorganizeTokens(List<string> tokens)
        {
            var result = new List<string>();
            var currentToken = string.Empty;

            foreach (var token in tokens)
            {
                currentToken += token;

                if (currentToken.Length >= 10)
                {
                    result.Add(currentToken);
                    currentToken = string.Empty;
                }
            }

            if (!string.IsNullOrEmpty(currentToken))
            {
                result.Add(currentToken);
            }

            return result;
        }

        /// <summary>
        /// Builds the Amais CMS page/feature integration context line for the chat based on the provided
        /// ChatContext. This line always states the current page alias and current feature ID explicitly —
        /// using the literal value "NONE" when either is unset — so the model always has an unambiguous
        /// signal about what is (or isn't) currently active, and provides instructions for how to handle
        /// queries related to these contexts, including which Amais plugin tool to call for feature data.
        /// </summary>
        /// <param name="chatContext"></param>
        /// <returns></returns>
        /// <remarks>
        /// This context line is specific to the Amais CMS page/feature integration surface and must only be
        /// invoked when Amais skills are enabled for the run (see <see cref="RunChatData.AmaisSkillsEnabled"/>
        /// and <see cref="RunVoiceChatData.AmaisSkillsEnabled"/>) — otherwise the model is instructed to call
        /// Amais-only tools that aren't actually available.
        /// </remarks>
        internal static string BuildAmaisContextLine(ChatContext chatContext, bool pageAnalyticsAvailable = false)
        {
            var hasPage = !string.IsNullOrEmpty(chatContext.PagePath);
            var hasFeature = !string.IsNullOrEmpty(chatContext.FeatureId);

            var pageAlias = hasPage ? chatContext.PagePath.TrimStart('/') : "NONE";
            var featureIdValue = hasFeature ? chatContext.FeatureId : "NONE";
            var featureToolName = chatContext.FeatureType switch
            {
                CommonFeatureMetadataConsts.TextFeatureType => "Get Text Feature",
                CommonFeatureMetadataConsts.GalleryFeatureType => "Get Gallery Feature",
                _ => "Get Feature Data",
            };

            var readFirst = new List<string>
            {
                $"- Current page alias: {pageAlias}",
                $"- Current feature ID: {featureIdValue}"
            };
            if (hasFeature)
            {
                if (!string.IsNullOrEmpty(chatContext.FeatureType))
                    readFirst.Add($"- Current feature type: {chatContext.FeatureType}");
                if (!string.IsNullOrEmpty(chatContext.FeatureName))
                    readFirst.Add($"- Current feature name: {chatContext.FeatureName}");
            }

            var pageOrFeature = hasPage && hasFeature ? "page alias and feature ID" : hasPage ? "page alias" : "feature ID";
            var thisPageOrFeature = hasPage && hasFeature ? "\"the current page\" or \"this feature,\"" : hasPage ? "\"the current page,\"" : "\"this feature,\"";
            if (hasPage || hasFeature)
            {
                readFirst.Add($"- Always reference this {pageOrFeature} for any related queries");
                readFirst.Add($"- When the {pageOrFeature} changes, treat it as your primary source of truth");
                readFirst.Add($"- Before answering questions about {thisPageOrFeature} acknowledge the {pageOrFeature} from this section");
            }

            var instructions = new List<string>();
            if (hasPage)
                instructions.Add("At the start of any page-related task, explicitly state: \"Current page: [PAGE_ALIAS]\"");
            if (hasFeature)
                instructions.Add("At the start of any feature-related task, explicitly state: \"Current feature: [FEATURE_NAME] (ID: [FEATURE_ID])\"");
            if (hasPage)
                instructions.Add("Use the Get Page Data tool with the current page alias as your first step for page queries");
            if (hasFeature)
                instructions.Add($"Use the {featureToolName} tool with the current feature ID ({chatContext.FeatureId}) for feature content queries");
            instructions.Add($"If the {pageOrFeature} changes between messages, immediately recognize and adapt");
            instructions.Add($"Never assume {pageOrFeature.Replace(" and ", " or ")} data from previous messages—always check current values");
            if (hasPage)
                instructions.Add("When retrieving page features, always fetch fresh data for the active page alias");
            if (pageAnalyticsAvailable)
                instructions.Add(hasPage
                    ? $"You can analyse how pages perform (views, engagement, bounce, recirculation and dead-page rates) and suggest improvements. When the user asks about the performance, traffic or engagement of the current page, use the Page Performance tool with page path {chatContext.PagePath}; use the Top Pages tool to compare it with the rest of the site. Only fetch analytics when the user asks for them"
                    : "You can analyse how pages perform (views, engagement, bounce, recirculation and dead-page rates) and suggest improvements with the Page Performance and Top Pages tools. Only fetch analytics when the user asks for them");
            if (hasFeature)
                instructions.Add("If a user asks about a DIFFERENT feature by name or ID, explicitly confirm the change before fetching");

            var sb = new StringBuilder();
            sb.AppendLine("CRITICAL CONTEXT - READ FIRST:");
            sb.AppendLine(string.Join("\n", readFirst));
            sb.AppendLine();
            sb.AppendLine("INSTRUCTIONS:");
            for (var i = 0; i < instructions.Count; i++)
                sb.AppendLine($"{i + 1}. {instructions[i]}");

            if (hasFeature)
            {
                var fragments = new List<string>();
                if (!string.IsNullOrEmpty(chatContext.FeatureType))
                    fragments.Add($"{chatContext.FeatureType} type");
                if (!string.IsNullOrEmpty(chatContext.FeatureName))
                    fragments.Add($"named \"{chatContext.FeatureName}\"");
                fragments.Add($"ID {chatContext.FeatureId}");

                sb.AppendLine();
                sb.AppendLine("CRITICAL - FEATURE DATA HANDLING:");
                sb.AppendLine($"- Current feature: {string.Join(", ", fragments)}");
                sb.AppendLine($"- ALWAYS use feature ID {chatContext.FeatureId} when retrieving \"current feature\" data");
                sb.AppendLine("- NEVER assume or fetch data for other feature IDs unless explicitly requested");
                sb.Append("- If unsure whether the user means the current feature or a different one, ask for clarification");
            }

            return sb.ToString();
        }



        /// <summary>
        /// Builds the <c>ChatCompletionAgent.Instructions</c> string shared by
        /// <see cref="RunChatCompletionAgentAsync"/> (text) and <see cref="RunVoiceChatCompletionAgentAsync"/>
        /// (voice) — previously copy-pasted between the two, which let them drift (Task #7032). The only
        /// behavioral difference between callers is <paramref name="isMaisyAgent"/>, which selects between
        /// <see cref="MaisyPromptConsiderations"/> (current, unrestricted behavior) and
        /// <see cref="NonMaisyPromptConsiderations"/> (deflects tool/plugin introspection questions without
        /// enumerating tool names or descriptions, while still answering genuine capability questions).
        /// </summary>
        internal static string BuildChatCompletionAgentInstructions(DateTimeOffset utcNow, string prompt, string amaisContextLine, string assistantReasoning, bool isMaisyAgent,
            string attachedDocumentsContext = null)
        {
            var previousReasoningText = !string.IsNullOrEmpty(assistantReasoning) ? assistantReasoning : "None.";
            var promptConsiderations = isMaisyAgent ? MaisyPromptConsiderations : NonMaisyPromptConsiderations;

            // Bug 8189 / Task 8219: the passages of the chat's large documents, for this turn only. They belong to the instructions,
            // which are not part of the chat history: never saved, never seen by the reducers or the memory handlers.
            var documentsSection = string.IsNullOrEmpty(attachedDocumentsContext)
                ? string.Empty
                : $"\n\n                    Attached documents:\n                    {attachedDocumentsContext}";

            return $@"Today: {utcNow:yyyy-MM-dd} {utcNow:HH:mm:ss} UTC
                    Prompt: {prompt}

                    {amaisContextLine}

                    Previous Reasoning Done: {previousReasoningText}

                    Prompt Considerations:
                    {promptConsiderations}

                    Rsponses Considerations:
                    {ResponseFormatConsiderations}{documentsSection}";
        }

        private static string BuildPriorityQAInjection(AMAIS.WKFLO.Common.MD.Domain.Entities.Agent agent, ToolPermissionPolicy toolPermissionPolicy = null)
        {
            if (agent.AgentTools == null || !agent.AgentTools.Any())
                return string.Empty;

            var sb = new StringBuilder();
            var hasItems = false;

            foreach (var agentTool in agent.AgentTools)
            {
                if (agentTool.Tool == null || agentTool.Tool.IsDeleted
                    || agentTool.Tool is not RunableTool runableTool || !runableTool.IsActive
                    || runableTool.Type != CommonToolTypeConsts.PriorityQA
                    || string.IsNullOrEmpty(runableTool.Config)
                    || IsBlocked(toolPermissionPolicy, runableTool))
                    continue;

                try
                {
                    var config = JsonConvert.DeserializeObject<PriorityQAToolConfig>(runableTool.Config);
                    if (config?.Items == null || config.Items.Count == 0) continue;

                    if (!hasItems)
                    {
                        sb.AppendLine();
                        sb.AppendLine(PriorityQAHeader);
                        sb.AppendLine(new string('=', 60));
                        hasItems = true;
                    }

                    foreach (var item in config.Items)
                    {
                        sb.AppendLine($"Q: {item.Question}");
                        sb.AppendLine($"A: {item.Answer}");
                        if (item.Links?.Count > 0)
                            sb.AppendLine($"Links: {string.Join(", ", item.Links)}");
                        sb.AppendLine();
                    }
                }
                catch { /* skip malformed config */ }
            }

            return hasItems ? sb.ToString() : string.Empty;
        }

        /// <summary>
        /// Whether the DXP page analytics skill can be used for this request: the deployment has a DXP MCP server
        /// configured and PetraCMS sent the <see cref="AmaisPluginCatalog.DxpPageAnalyticsCode"/> capability.
        /// </summary>
        internal static bool IsPageAnalyticsAvailable(ChatContext chatContext) =>
            DxpPagePlugin.IsMcpServerConfigured()
            && AmaisPluginCatalog.IsOfferable(AmaisPluginCatalog.PageAnalyticsName, chatContext?.AmaisAvailableEndpoints);
        private static readonly string[] AmaisSkillsDisabledCatalogNames = ["Knowledge"];

        internal static string BuildAmaisAgentCapabilitiesInjection(AMAIS.WKFLO.Common.MD.Domain.Entities.Agent agent, ChatContext chatContext, ToolPermissionPolicy toolPermissionPolicy = null)
        {
            if (agent.AgentTools == null || !agent.AgentTools.Any())
                return string.Empty;

            var sb = new StringBuilder();
            var hasItems = false;

            void AppendBullet(string name, string description)
            {
                if (!hasItems)
                {
                    sb.AppendLine();
                    sb.AppendLine(AvailableToolsHeader);
                    hasItems = true;
                }

                sb.AppendLine(string.IsNullOrWhiteSpace(description) ? $"- {name}" : $"- {name}: {description}");
            }

            foreach (var agentTool in agent.AgentTools)
            {
                if (agentTool.Tool == null || agentTool.Tool.IsDeleted
                    || agentTool.Tool is not RunableTool runableTool || !runableTool.IsActive
                    || IsBlocked(toolPermissionPolicy, runableTool))
                    continue;

                if (runableTool.Type == CommonToolTypeConsts.IntegrationAmais)
                {
                    // The skill selector loads Welcome/Post/Page/GalleryFeature/TextFeature into the kernel
                    // lazily, on demand per message (see AmaisSkillSelectorPlugin/AddWkflowToolsAsync) — so
                    // the kernel only ever reflects whichever subset got selected THIS turn, not everything
                    // the tenant's AMAIS integration could offer. List the static catalog instead of relying
                    // on runtime kernel state, so "what can you help with" is answered fully even before any
                    // specific skill has been selected. AmaisKnowledgePlugin (the "Knowledge" entry) is
                    // registered unconditionally regardless of AreSkillsEnabled, so it's always included.
                    AmaisIntegrationToolConfig config;
                    try { config = JsonConvert.DeserializeObject<AmaisIntegrationToolConfig>(runableTool.Config); }
                    catch { config = null; }

                    IReadOnlyList<string> candidateNames = config != null && config.AreSkillsEnabled
                        ? [.. AmaisSkillsDisabledCatalogNames, .. AmaisPluginCatalog.GetSkillSelectorCandidateNames(DxpPagePlugin.IsMcpServerConfigured())]
                        : AmaisSkillsDisabledCatalogNames;

                    foreach (var entry in AmaisPluginCatalog.GetOfferable(chatContext.AmaisAvailableEndpoints, candidateNames))
                        AppendBullet(entry.Name, entry.Description);

                    continue;
                }

                AppendBullet(runableTool.Name, runableTool.Description);
            }

            return hasItems ? sb.ToString() : string.Empty;
        }

        // WI 7575: a blocked (non-MCP) tool must not leak into the prompt either - e.g. PriorityQA answers or
        // the ContactForm "mandatory call" instructions for a tool the model cannot call.
        internal static bool IsBlocked(ToolPermissionPolicy toolPermissionPolicy, RunableTool tool) =>
            toolPermissionPolicy != null
            && tool.Type != CommonToolTypeConsts.MCP
            && toolPermissionPolicy.GetToolPermission(tool.Id) == AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions.ToolPermissionValues.Block;

        private async Task<string> BuildContactFormInjectionAsync(AMAIS.WKFLO.Common.MD.Domain.Entities.Agent agent, string tenantName, string subjectId, ToolPermissionPolicy toolPermissionPolicy = null)
        {
            if (agent.AgentTools == null || !agent.AgentTools.Any())
                return string.Empty;

            var sb = new StringBuilder();
            var hasItems = false;

            foreach (var agentTool in agent.AgentTools)
            {
                if (agentTool.Tool == null || agentTool.Tool.IsDeleted
                    || agentTool.Tool is not RunableTool runableTool || !runableTool.IsActive
                    || runableTool.Type != CommonToolTypeConsts.ContactForm
                    || string.IsNullOrEmpty(runableTool.Config)
                    || IsBlocked(toolPermissionPolicy, runableTool))
                    continue;

                try
                {
                    if (!string.IsNullOrEmpty(tenantName)
                        && !string.IsNullOrEmpty(subjectId)
                        && Guid.TryParse(subjectId, out var cfSubjectGuid)
                        && await _contactFormLogService.ExistsAsync(cfSubjectGuid, tenantName))
                    {
                        return ContactFormAlreadyCollectedMessage;
                    }

                    var config = JsonConvert.DeserializeObject<ContactFormToolConfig>(runableTool.Config);
                    if (config == null || string.IsNullOrWhiteSpace(config.WhenToUse)) continue;

                    if (!hasItems)
                    {
                        sb.AppendLine();
                        sb.AppendLine(ContactFormMandatoryHeader);
                        sb.AppendLine(new string('=', 60));
                        hasItems = true;
                    }

                    var pluginName = $"plugin_{runableTool.Id}_{runableTool.Type}";
                    sb.AppendLine(string.Format(ContactFormToolAccessTemplate, pluginName));
                    sb.AppendLine($"When to call it: {config.WhenToUse}");
                    sb.AppendLine(ContactFormCallInstructions);
                    sb.AppendLine(new string('-', 50));
                }
                catch { /* skip malformed config */ }
            }

            return hasItems ? sb.ToString() : string.Empty;
        }

        // publish_agent_event is gated on RunnableAgent.EnableSpinUpAgents alone (no Tool/Tool.Config —
        // see AgentEventPublisherPlugin) and, under FunctionChoiceBehavior.Auto(), the model has no
        // ground truth for which agent names are actually valid targets. This builds two independent
        // prompt segments from the same allowlist fetch:
        //  1. An always-on catalog (name + description) telling the model it MUST call publish_agent_event itself
        //     when it decides to delegate, even when the user never typed an explicit @mention (Story
        //     #6688 follow-up, Bug 8007). The names are also returned (TargetNames) for the miss-detection warning.
        //  2. The original reactive block: when the raw message already @mentions one of these agents by
        //     name, force the call and hand back the exact matched name(s), mirroring
        //     BuildContactFormInjectionAsync's "MANDATORY TOOL CALL" pattern.
        private async Task<(string Prompt, IReadOnlyList<string> TargetNames)> BuildAgentEventPublisherInjectionAsync(AMAIS.WKFLO.Common.MD.Domain.Entities.Agent agent, string message)
        {
            if (agent is not RunnableAgent { EnableSpinUpAgents: true } runnableAgent)
                return (string.Empty, []);

            var allowedTargets = await QueryActiveAllowedSpinUpTargets(
                    _db.AgentAllowedSpinUpAgents.AsNoTracking().Cacheable(), runnableAgent.Id)
                .ToListAsync();

            if (allowedTargets.Count == 0)
                return (string.Empty, []);

            return (BuildAgentHandoffPrompt(allowedTargets, message), allowedTargets.Select(t => t.Name).ToList());
        }

        // Pure prompt building, separate from the DB lookup so it can be unit tested without mocking
        // IMasterDataDbContext. Agents are listed by plain name (not "@Name"): the user does not need to type @
        // for the model to delegate (Bug 8007), and the "@" form invited it to suggest the mention instead of
        // calling the tool. The @-mention detection and the mandatory block are unchanged.
        internal static string BuildAgentHandoffPrompt(IReadOnlyList<AllowedSpinUpAgentTarget> allowedTargets, string message)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine(AgentHandoffsHeader);
            sb.AppendLine(new string('=', 60));
            sb.AppendLine(AgentHandoffsDescription);
            foreach (var target in allowedTargets)
            {
                var description = string.IsNullOrWhiteSpace(target.Description) ? "(no description provided)" : target.Description;
                sb.AppendLine($"- {target.Name}: {description}");
            }

            if (!string.IsNullOrWhiteSpace(message) && message.Contains('@'))
            {
                var mentionedNames = ExtractMentionedAgentNames(message, allowedTargets.Select(t => t.Name));
                if (mentionedNames.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(AgentHandoffMandatoryHeader);
                    sb.AppendLine(new string('=', 60));
                    sb.AppendLine(string.Format(AgentHandoffMentionedTemplate, string.Join(", ", mentionedNames)));
                    sb.AppendLine(AgentHandoffCallInstructions);
                }
            }

            return sb.ToString();
        }

        // Bug 8007: allowlisted agents whose names the final reply mentions although publish_agent_event was never
        // called this turn, i.e. the reply probably announced a hand-off that did not happen. Empty when the tool
        // was called, the turn stopped for a tool approval (the approval filter stops the call before the plugin body
        // runs, so the flag stays false) or no target is named. Whole-word, case-insensitive name match: a heuristic
        // used only for a warning log.
        internal static List<string> FindAnnouncedButUncalledHandoffs(string reply, IEnumerable<string> targetNames, bool publishCalled, bool approvalRequested = false)
        {
            if (publishCalled || approvalRequested || string.IsNullOrWhiteSpace(reply) || targetNames == null)
                return [];

            return targetNames
                .Where(name => !string.IsNullOrWhiteSpace(name)
                    && System.Text.RegularExpressions.Regex.IsMatch(reply, @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(name) + @"(?!\w)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Pure name-matching kept separate from the DB lookup above so it can be unit tested without
        // mocking IMasterDataDbContext. Exact "@" + allowlisted-name substring match, case-insensitive —
        // deliberately not a regex tokenizer, since agent names may contain spaces/punctuation and the
        // plugin itself already requires an exact Agent.Name match to resolve a target.
        internal static List<string> ExtractMentionedAgentNames(string message, IEnumerable<string> allowedNames)
        {
            if (string.IsNullOrWhiteSpace(message) || allowedNames == null)
                return [];

            return allowedNames
                .Where(name => !string.IsNullOrWhiteSpace(name) && message.Contains("@" + name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Allowlisted hand-off targets the model may be told about: not deleted AND active (WI 7318
        // hardening). An inactive target is already rejected at call time by AgentEventPublisherPlugin's
        // own IsActive check, so advertising it in the catalog would only invite a hand-off that fails.
        // Allowlist targets are always RunnableAgent rows (TenantAgent/PublishedTenantAgent).
        internal static IQueryable<AllowedSpinUpAgentTarget> QueryActiveAllowedSpinUpTargets(IQueryable<AgentAllowedSpinUpAgent> source, int agentId) =>
            WhereActiveAllowedSpinUpAgents(source, agentId)
                .Select(m => new AllowedSpinUpAgentTarget(m.AllowedSpinUpAgent.Name, m.AllowedSpinUpAgent.Description));

        /// <summary>
        /// Allowlist rows of <paramref name="agentId"/> whose target is not deleted and active. Shared by the hand-off
        /// catalog above and <c>LoadSiblingAgentContextBySessionQueryHandler</c>.
        /// </summary>
        /// <remarks>
        /// IsActive is declared on the abstract <see cref="RunnableAgent"/>, which has no mapping/discriminator of its
        /// own in the TPH model: a type test or cast to it ("is RunnableAgent" / "(RunnableAgent)x") cannot be translated
        /// to SQL and throws on a real provider (it only works under LINQ-to-objects). The type tests and casts below
        /// use the concrete mapped types instead, which EF Core translates to a discriminator check plus the shared
        /// column. See ActiveAllowedSpinUpAgentsRelationalTests (real SQLite provider).
        /// </remarks>
        internal static IQueryable<AgentAllowedSpinUpAgent> WhereActiveAllowedSpinUpAgents(IQueryable<AgentAllowedSpinUpAgent> source, int agentId) =>
            source.Where(m => m.AgentId == agentId
                && !m.AllowedSpinUpAgent.IsDeleted
                && ((m.AllowedSpinUpAgent is TenantAgent && ((TenantAgent)m.AllowedSpinUpAgent).IsActive)
                    || (m.AllowedSpinUpAgent is PublishedTenantAgent && ((PublishedTenantAgent)m.AllowedSpinUpAgent).IsActive)));

        internal readonly record struct AllowedSpinUpAgentTarget(string Name, string Description);

        /// <summary>
        /// US 7078: strips out any ephemeral sibling-agent context messages (see
        /// LoadSiblingAgentContextBySessionQueryHandler / LoadChatHistoryBySessionIdAsync) before the
        /// turn's chat history is published for persistence. Those messages were appended only to steer
        /// this turn's model call — they must never reach <c>SaveChatAsync</c>, otherwise the sibling's
        /// content would leak into the root agent's own transcript as if the root had said/produced it
        /// itself, and every future turn's re-pull of the sibling's full history would keep re-adding it.
        /// </summary>
        /// <remarks>
        /// US 7502: <paramref name="detachedStoppedMarkers"/> are the "Stopped" markers taken out of the history when it
        /// was loaded (so the model never sees them); they are put back here, in timestamp order.
        /// </remarks>
        internal static ChatHistory BuildPersistableChatHistory(ChatHistory chatHistory, IReadOnlyList<ChatMessageContent> detachedStoppedMarkers = null) =>
            StoppedTurnMarker.Reattach(
                new(SearchAttachedDocumentsPlugin.OmitPassages(chatHistory.Where(m => m.Metadata == null || !m.Metadata.ContainsKey(CommonMetadataConsts.IsEphemeralAgentContext))).ToList()),
                detachedStoppedMarkers);

        #endregion
    }
}

