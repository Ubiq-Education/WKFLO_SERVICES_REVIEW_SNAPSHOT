using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.GalleryFeature;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Page;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.PageAnalytics;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.SkillSelector;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.TextFeature;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ContactForm;
using AMAIS.WKFLO.Common.AI.Application.Plugins.HttpCall;
using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Knowledge;
using AMAIS.WKFLO.Common.AI.Application.Plugins.PriorityQA;
using AMAIS.WKFLO.Common.AI.Application.Plugins.TextSuggestion;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    public static class SemanticKernelExtensions
    {
        // Anthropic rejects a 'system' role message anywhere in the messages array unless it's at
        // index 0 or immediately follows a user/assistant turn. Auto-invoked context (page/text/gallery
        // data, knowledge) used to each append their own trailing system message, so any turn where more
        // than one fired produced two adjacent system messages - the second one violates that rule. All
        // of them now share a single system message pinned at chatHistory[0], split into named sections
        // so each source can update only its own section without clobbering the others.
        private const string SystemMessageSectionsMetadataKey = "SystemMessageSections";

        private static bool TryGetSystemMessageSections(ChatHistory chatHistory, out ChatMessageContent systemMessage, out Dictionary<string, string> sections)
        {
            if (chatHistory.Count > 0 && chatHistory[0].Role == AuthorRole.System)
            {
                systemMessage = chatHistory[0];
                sections = systemMessage.Metadata != null
                    && systemMessage.Metadata.TryGetValue(SystemMessageSectionsMetadataKey, out var raw)
                    && raw is Dictionary<string, string> existingSections
                        ? existingSections
                        : new Dictionary<string, string>();
                return true;
            }

            systemMessage = null;
            sections = new Dictionary<string, string>();
            return false;
        }

        // Internal (not private): also called directly from SemanticKernelAgentServiceRun.cs for the
        // TopicHandoffMessage system message, so it goes through the same index-0 pinning as the
        // AutoInvoke*Async sources here instead of being appended mid-conversation.
        internal static void UpsertSystemMessageSection(ChatHistory chatHistory, string sectionKey, string sectionContent, IDictionary<string, object> metadata)
        {
            if (!TryGetSystemMessageSections(chatHistory, out var systemMessage, out var sections))
            {
                systemMessage = new ChatMessageContent(AuthorRole.System, string.Empty);
                chatHistory.Insert(0, systemMessage);
            }

            sections[sectionKey] = sectionContent;
            systemMessage.Content = string.Join("\n\n", sections.Values);

            var mergedMetadata = metadata != null ? new Dictionary<string, object>(metadata) : new Dictionary<string, object>();
            mergedMetadata[SystemMessageSectionsMetadataKey] = sections;
            systemMessage.Metadata = mergedMetadata;
        }

        // Conversations persisted before this fix may have AuthorRole.System messages scattered
        // anywhere in history (Anthropic now rejects one unless it's the first entry or immediately
        // follows a user/assistant turn ending in a server tool result). Called when loading a
        // conversation to continue it, so a resumed old chat doesn't immediately fail - a valid
        // index-0 system message (the current pattern) is left untouched.
        internal static void RemoveMidConversationSystemMessages(ChatHistory chatHistory)
        {
            for (var i = chatHistory.Count - 1; i >= 1; i--)
            {
                if (chatHistory[i].Role == AuthorRole.System)
                {
                    chatHistory.RemoveAt(i);
                }
            }
        }

        // Not all Anthropic-hosting platforms reliably auto-merge consecutive same-role turns (Bedrock
        // has had bugs specifically around this), and this codebase can now produce runs of several
        // consecutive AuthorRole.Assistant messages (BaseAmaisPlugin, reasoning/image-analysis, the
        // real reply) or AuthorRole.User messages (a text message plus a separate file/image message).
        //
        // Takes IEnumerable<ChatMessageContent> rather than ChatHistory specifically so it can merge
        // any sequence of messages - a full ChatHistory, a LINQ-filtered/projected subset, etc. -
        // and always returns a fresh ChatHistory regardless of what was passed in. Items from every
        // message in a run are concatenated (so images/text all survive as separate content blocks
        // within the one merged message; Anthropic supports multiple blocks per message, so nothing
        // needs to be joined into a single string), and Metadata keeps the first message's values,
        // filling in any keys the first message didn't have from later ones in the run.
        //
        // Never mutates the input - this is meant only for building a specific outgoing LLM request,
        // never for the persisted/displayed ChatHistory. Merging collapses distinct MessageIds and
        // per-message RenderAsSystem/ChannelMessageType flags the chat UI relies on to render each
        // original message correctly.
        internal static ChatHistory MergeConsecutiveSameRoleMessages(IEnumerable<ChatMessageContent> chatMessages)
        {
            var mergedHistory = new ChatHistory();
            List<ChatMessageContent> currentRun = null;

            void FlushRun()
            {
                if (currentRun == null)
                {
                    return;
                }

                if (currentRun.Count == 1)
                {
                    mergedHistory.Add(currentRun[0]);
                }
                else
                {
                    var items = new ChatMessageContentItemCollection();
                    var mergedMetadata = new Dictionary<string, object>();

                    foreach (var message in currentRun)
                    {
                        foreach (var item in message.Items)
                        {
                            items.Add(item);
                        }

                        if (message.Metadata == null)
                        {
                            continue;
                        }

                        foreach (var (key, value) in message.Metadata)
                        {
                            mergedMetadata.TryAdd(key, value);
                        }
                    }

                    mergedHistory.Add(new ChatMessageContent(currentRun[0].Role, items, metadata: mergedMetadata));
                }

                currentRun = null;
            }

            foreach (var message in chatMessages)
            {
                var isMergeable = message.Role == AuthorRole.Assistant || message.Role == AuthorRole.User;

                if (isMergeable && currentRun != null && currentRun[0].Role == message.Role)
                {
                    currentRun.Add(message);
                    continue;
                }

                FlushRun();

                if (isMergeable)
                {
                    currentRun = new List<ChatMessageContent> { message };
                }
                else
                {
                    mergedHistory.Add(message);
                }
            }

            FlushRun();

            return mergedHistory;
        }

        public static async Task<string> GetWkflowPromptAsync(this Kernel kernel, MD.Domain.Entities.Agent agent)
        {
            var prompt = agent.SystemPrompt;

            if (agent.AgentTools != null && agent.AgentTools.Any())
            {
                foreach (var agentTool in agent.AgentTools)
                {
                    if (agentTool.Tool != null && !agentTool.Tool.IsDeleted
                        && (agentTool.Tool is RunableTool runableTool && runableTool.IsActive))
                    {
                        (prompt, _) = ParsePrompt(prompt, runableTool);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(agent.ResponseTone))
            {
                prompt += $"\nRespond in a {agent.ResponseTone} tone.";
            }

            prompt = AppendAdditionalInstructions(prompt, agent);

            return prompt;
        }

        /// <summary>
        /// Fixed header that introduces the administrator's <see cref="RunnableAgent.AdditionalInstructions"/>
        /// in the system prompt (WI 7318).
        /// </summary>
        internal const string AdditionalInstructionsHeader = "Additional instructions from the administrator:";

        /// <summary>
        /// WI 7318: appends <see cref="RunnableAgent.AdditionalInstructions"/> after the platform-owned
        /// SystemPrompt (and its tone line) under <see cref="AdditionalInstructionsHeader"/>. Deliberately NOT
        /// run through <see cref="ParsePrompt"/>: it is plain admin text, so a literal <c>{{tool:...}}</c>
        /// typed by an admin is never turned into a plugin reference. It also needs no template escaping:
        /// the result only ever reaches <c>ChatCompletionAgent.Instructions</c> with no <c>Template</c> set,
        /// and SK passes such instructions through verbatim (no <c>{{...}}</c> rendering).
        /// <c>SemanticKernelAgentService.BuildChatCompletionAgentInstructions</c> places this whole prompt
        /// above the platform "Prompt Considerations" guardrails, so those still have the final word.
        /// </summary>
        internal static string AppendAdditionalInstructions(string prompt, MD.Domain.Entities.Agent agent)
        {
            if (agent is not RunnableAgent { AdditionalInstructions: var additionalInstructions }
                || string.IsNullOrWhiteSpace(additionalInstructions))
                return prompt;

            return $"{prompt}\n\n{AdditionalInstructionsHeader}\n{additionalInstructions.Trim()}";
        }

        public static async Task<bool> AddWkflowToolsAsync(this Kernel kernel, MD.Domain.Entities.Agent agent,
            IDatastreamProvider datastreamProvider,
            IChatNotificationService notificationService,
            Microsoft.Extensions.Options.IOptions<Models.Settings.ElasticsearchSettings> elasticsearchSettings,
            IMcpClientService mcpService,
            Contexts.TenantContext tenantContext,
            Contexts.ChatContext chatContext,
            Contexts.UserContext userContext,
            List<string> agentTagNames,
            IWkfloMapper mapper,
            IDateTimeProvider dateTimeProvider,
            ILogger logger,
            IBlobStorageProvider blobStorageProvider = null,
            IWkfloMediator mediator = null,
            IContactFormPluginLogService contactFormLogService = null,
            IMasterDataDbContext db = null,
            bool? rootIsWidget = null,
            Contexts.AgentSharableContext agentSharableContext = null,
            ToolPermissionPolicy toolPermissionPolicy = null,
            bool handOffApprovalsAvailable = false,
            IChatStopCheckpoint chatStopCheckpoint = null,
            IChatTurnErrorReporter chatTurnErrorReporter = null)
        {
            var amaisSkillsEnabled = false;
            var maxOutputLengthsByToolId = new Dictionary<int, int?>();

            // WI 7447/7575: no policy passed means nobody can approve anything in this run, so Ask acts as Block.
            var permissionPolicy = toolPermissionPolicy ?? ToolPermissionPolicy.Build(agent, approvalsAvailable: false);

            if (agent.AgentTools != null && agent.AgentTools.Any())
            {
                foreach (var agentTool in agent.AgentTools)
                {
                    if (agentTool.Tool != null && !agentTool.Tool.IsDeleted
                        && (agentTool.Tool is RunableTool runableTool && runableTool.IsActive))
                    {
                        // WI 7575: a blocked non-MCP tool is never offered to the model - for Integration_Amais this
                        // also means its knowledge plugin, skill selector (lazy plugins) and text suggestion. MCP tools
                        // are filtered per function inside AddWkflowMcpToolAsync instead.
                        if (runableTool.Type != CommonToolTypeConsts.MCP
                            && permissionPolicy.GetToolPermission(runableTool.Id) == ToolPermissionValues.Block)
                        {
                            logger.LogInformation("Tool {ToolId} is blocked for agent {AgentId}; not registering it", runableTool.Id, agent.Id);
                            continue;
                        }

                        maxOutputLengthsByToolId[runableTool.Id] = runableTool.MaxToolOutputLength;

                        var pluginName = string.Concat("plugin_", agentTool.Tool.Id, "_", agentTool.Tool.Type);

                        switch (agentTool.Tool.Type)
                        {
                            case CommonToolTypeConsts.Knowledge:
                                kernel.Plugins.AddFromObject(new KnowledgePlugin(runableTool, datastreamProvider, notificationService, elasticsearchSettings, tenantContext, chatContext, logger), pluginName);
                                break;
                            case CommonToolTypeConsts.PriorityQA:
                                kernel.Plugins.AddFromObject(new PriorityQAPlugin(runableTool, notificationService, chatContext, logger), pluginName);
                                break;
                            case CommonToolTypeConsts.ChatClassification:
                                // Classification runs automatically before each message — no SK plugin needed
                                break;
                            case CommonToolTypeConsts.MCP:
                                await kernel.AddWkflowMcpToolAsync(runableTool, mcpService, pluginName, userContext, chatContext, agentSharableContext,
                                    notificationService, agent.Id, agent.Name, dateTimeProvider, logger, permissionPolicy, chatTurnErrorReporter);
                                break;
                            case CommonToolTypeConsts.IntegrationAmais:
                                var toolResult = mapper.Map<ToolResult>(agentTool.Tool);

                                kernel.Plugins.AddFromType<AmaisKnowledgePlugin>(pluginName + "_Knowledge", kernel.Services);

                                if (toolResult.AmaisIntegrationToolConfiguration != null && toolResult.AmaisIntegrationToolConfiguration.AreSkillsEnabled)
                                {
                                    amaisSkillsEnabled = true;

                                    var availableSkillSelectorPlugins = AmaisPluginCatalog.GetOfferable(
                                        chatContext.AmaisAvailableEndpoints,
                                        AmaisPluginCatalog.GetSkillSelectorCandidateNames(DxpPagePlugin.IsMcpServerConfigured()));

                                    if (availableSkillSelectorPlugins.Count > 0)
                                    {
                                        kernel.Data[pluginName + "_SkillSelector"] = new AmaisSkillSelectorPlugin(kernel, pluginName, logger, availableSkillSelectorPlugins, chatContext.AmaisAvailableEndpoints);
                                    }

                                    // suggest_text_edit rides on the same AreSkillsEnabled gate as get_text_feature
                                    // (via the skill selector above) rather than being its own Tool type — it's the
                                    // write/suggest counterpart to that read tool and needs no separate provisioning.
                                    var textSuggestionPlugin = new TextSuggestionPlugin(runableTool, notificationService, chatContext, dateTimeProvider, logger);
                                    var textSuggestionFunction = KernelFunctionFactory.CreateFromMethod(
                                        textSuggestionPlugin.SuggestTextEditAsync,
                                        "suggest_text_edit",
                                        "Suggest an edit to a text feature the user has selected on the page. Call this after the user asks to edit, rewrite, or add to a selected text feature — fetch the feature's current text with get_text_feature first if you haven't already."
                                    );
                                    kernel.Plugins.AddFromFunctions(pluginName + "_TextSuggestion", [textSuggestionFunction]);
                                }
                                break;
                            case CommonToolTypeConsts.ContactForm:
                                var contactFormConfig = JsonConvert.DeserializeObject<ContactFormToolConfig>(runableTool.Config);
                                var contactFormPlugin = new ContactFormPlugin(runableTool, notificationService, chatContext, logger, contactFormLogService, tenantContext.TenantName);
                                var contactFormFunctionDescription = contactFormConfig?.WhenToUse ?? "Display a contact form to collect user contact information";
                                if (contactFormLogService != null
                                    && !string.IsNullOrEmpty(tenantContext.TenantName)
                                    && !string.IsNullOrEmpty(chatContext.SubjectId)
                                    && Guid.TryParse(chatContext.SubjectId, out var cfSubjectGuid)
                                    && await contactFormLogService.ExistsAsync(cfSubjectGuid, tenantContext.TenantName))
                                {
                                    contactFormFunctionDescription = "The user's contact information has already been collected. Do NOT call this function. Answer the user's question directly without requesting registration.";
                                }
                                var contactFormFunction = KernelFunctionFactory.CreateFromMethod(
                                    contactFormPlugin.RequestContactFormAsync,
                                    "request_contact_form",
                                    contactFormFunctionDescription
                                );
                                kernel.Plugins.AddFromFunctions(pluginName, [contactFormFunction]);
                                break;
                            case CommonToolTypeConsts.Http:
                                kernel.Plugins.Add(HttpCallPlugin.BuildKernelPlugin(runableTool, notificationService, chatContext, pluginName, logger));
                                break;
                            case CommonToolTypeConsts.ImageGeneration:
                                if (blobStorageProvider != null && mediator != null)
                                {
                                    try
                                    {
                                        var agentType = agentTagNames != null && agentTagNames.Any() ? string.Join("_", agentTagNames) : "ImageGeneration_" + runableTool.Id;
                                        kernel.Plugins.Add(ImageGenerationPlugin.BuildKernelPlugin(runableTool, notificationService, chatContext, tenantContext, blobStorageProvider, mediator, dateTimeProvider, logger, pluginName, agentType));
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "Skipping ImageGeneration tool '{ToolName}' — invalid configuration", runableTool.Name);
                                    }
                                }
                                break;
                            default:
                                // Add other tools here
                                break;
                        }
                    }
                }
            }

            // AgentEventPublisherPlugin isn't tied to a Tool/Tool.Config at all — it's gated purely by
            // RunnableAgent.EnableSpinUpAgents, a tenant-admin-controlled flag on the agent itself.
            // When set, the calling agent's own LLM can hand a message off to one or more other
            // agents by name (a JSON array of targets); the plugin resolves each name to a real agent
            // id itself, scoped to the current tenant. There is no LLM-triggered path to spin up a
            // full multi-agent orchestration from inside a running agent — only the HTTP endpoint
            // (POST /api/v1/agents/orchestrator) can do that.
            if (mediator != null && db != null && agent is RunnableAgent { EnableSpinUpAgents: true })
            {
                kernel.Plugins.AddFromObject(
                    new Plugins.AgentEventPublisher.AgentEventPublisherPlugin(mediator, notificationService, chatContext, tenantContext, db, logger, agent.Id, blobStorageProvider, rootIsWidget, handOffApprovalsAvailable, chatStopCheckpoint),
                    "plugin_spin_up_agent_event_publisher");
            }

            // WI 7447/7575 defense in depth: refuses any blocked / not-allowlisted call that still reaches
            // invocation. Added before the output-size filter so it wraps it (see that filter's ordering remarks).
            if (agent.AgentTools != null && agent.AgentTools.Any())
            {
                kernel.FunctionInvocationFilters.Add(new ToolPermissionEnforcementFilter(permissionPolicy));
            }

            // Added directly to the already-built kernel (not via global DI in AIProviderHelper) so its
            // per-tool thresholds can reflect this turn's actual RunableTool rows, and so it lands after
            // any DI-sourced filter (e.g. SemanticKernelInvocationLogger) in the invocation chain — see
            // SemanticKernelLargeToolOutputFilter's remarks for why that ordering matters. Skipped entirely
            // when nothing would trigger it, so a turn with no tool-level cap configured doesn't pay for an
            // inert filter.
            if (maxOutputLengthsByToolId.Values.Any(v => v.HasValue))
            {
                kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(maxOutputLengthsByToolId));
            }

            return amaisSkillsEnabled;
        }

        private static (string, string) ParsePrompt(string prompt, RunableTool runableTool)
        {
            var pluginName = string.Concat("plugin_", runableTool.Id, "_", runableTool.Type);
            var pattern = $@"\{{{{tool:{runableTool.Id}:.*?\}}}}";
            prompt = System.Text.RegularExpressions.Regex.Replace(prompt, pattern, pluginName + " Plugin");
            return (prompt, pluginName);
        }

        private static async Task AddWkflowMcpToolAsync(this Kernel kernel, RunableTool runableTool, IMcpClientService mcpService, string pluginName,
            Contexts.UserContext userContext, Contexts.ChatContext chatContext, Contexts.AgentSharableContext agentSharableContext,
            IChatNotificationService notificationService,
            int agentId, string agentName, IDateTimeProvider dateTimeProvider, ILogger logger, ToolPermissionPolicy permissionPolicy,
            IChatTurnErrorReporter chatTurnErrorReporter = null)
        {
            // Task 7961: read inside the try, so a broken config only skips this tool (with a warning to the chat).
            McpServerConfig serverConfig = null;
            try
            {
                serverConfig = JsonConvert.DeserializeObject<McpServerConfig>(runableTool.Config);
                serverConfig.Name = runableTool.Name;

                // For a McpOAuthScope.PerUser tool (e.g. Canva), resolves to the calling user's own id so the
                // token lookup below matches the row they connected via the admin dashboard — same resolution
                // MD API's Status/Disconnect handlers use, see McpOAuthScopeResolver's remarks. userContext.Guid
                // is already reliably populated here for any normally-authenticated caller (e.g. an admin
                // testing the agent from the dashboard) via the AI API's own UserContextResolver +
                // AutoUserCreationMiddleware pipeline — this was never actually blocked on Bug 7163/Task 7164,
                // which is specifically about the public chat widget's own JWT-validation timing. For the
                // widget path, userContext.Guid still resolves to empty until that bug is fixed, which throws
                // McpOAuthPerUserIdentityUnavailableException below rather than silently resolving null — see
                // the dedicated catch for it: this tool is simply skipped, not the whole turn.
                var requestingUserId = McpOAuthScopeResolver.ResolveRequestingUserId(runableTool.Config, userContext?.Guid);
                mcpService.AddServerConfig(serverConfig, runableTool.McpOAuthConnectionId, requestingUserId);
                var connected = await mcpService.ConnectAsync(serverConfig.Id);
                if (connected)
                {
                    logger.LogInformation("Successfully connected to {ServerName}", serverConfig.Name);
                    IList<McpClientTool> tools = await mcpService.ListToolsAsync(serverConfig.Id);
                    //var pluginName = string.Concat("plugin_", tool.Id, "_", tool.Type);
                    //var pattern = $@"\{{{{tool:{tool.Id}:.*?\}}}}";
                    //prompt = System.Text.RegularExpressions.Regex.Replace(prompt, pattern, pluginName + " Plugin");
                    // WI 7447/7575: MCP has no way to hide functions server-side, so only functions that are on the
                    // tool's allowlist AND not blocked for this agent are ever registered. Nothing allowed = no plugin.
                    // Bug 7856: the allowlist only applies when the tool's permissions are managed, and a tool with no
                    // enabled function has every function on it.
                    var allowedTools = FilterAllowedMcpFunctions(tools, runableTool.Id, permissionPolicy);
                    if (allowedTools.Count > 0)
                    {
                        kernel.Plugins.AddFromFunctions(pluginName, allowedTools.Select(aiFunction => aiFunction.AsKernelFunction()));
                    }
                    else
                    {
                        logger.LogInformation("No allowed functions on MCP tool {ToolId}; not registering it", runableTool.Id);
                    }
                }
                else
                {
                    logger.LogWarning("Failed to connect to {ServerName}", serverConfig.Name);
                    await ReportMcpToolUnavailableAsync(chatTurnErrorReporter, pluginName, agentId, agentName);
                }
            }
            catch (McpOAuthInteractiveConsentRequiredException)
            {
                // Expected, not a bug: this tool's cached token can't be silently refreshed and needs a human
                // to go through interactive consent again. This background connection path has no browser to
                // drive that through, so — unlike McpOAuthPerUserIdentityUnavailableException below, which is
                // silent — let the chat user know, with a link to reconnect it when we can build one (real
                // WKFLO identity + public-widget shareId available; see SendMcpOAuthReconnectNotificationAsync).
                // Skip loading just this tool either way — every other tool/plugin on the agent still loads.
                var sentReconnectLink = await SendMcpOAuthReconnectNotificationAsync(
                    runableTool, serverConfig, chatContext, agentSharableContext, userContext,
                    notificationService, agentId, agentName, dateTimeProvider);

                logger.LogWarning(
                    sentReconnectLink
                        ? "Skipping {ServerName}: needs interactive OAuth consent — sent a reconnect link to the chat"
                        : "Skipping {ServerName}: needs interactive OAuth consent, but no reconnect link could be built for this session — sent a fallback message instead",
                    serverConfig.Name);
            }
            catch (McpOAuthPerUserIdentityUnavailableException)
            {
                // Expected, not a bug: this tool is per-user-scoped but no WKFLO identity could be resolved
                // for the current caller (e.g. the public chat widget, still blocked on Bug 7163/Task 7164).
                // Skip loading just this tool — every other tool/plugin on the agent still loads normally.
                logger.LogWarning("Skipping {ServerName}: per-user OAuth tool with no resolvable caller identity", serverConfig.Name);
            }
            catch (Exception ex)
            {
                // US 7959 / Task 7961: the full error stays in the logs; the chat only gets the generic, non-terminal
                // "tool not available" warning and the turn goes on without this tool.
                logger.LogError(ex, "Error connecting to {ServerName}", serverConfig?.Name ?? runableTool.Name);
                await ReportMcpToolUnavailableAsync(chatTurnErrorReporter, pluginName, agentId, agentName);
            }
        }

        /// <summary>
        /// US 7959 / Task 7961: an MCP tool could not be loaded for this turn (connect, list or config failure): one
        /// "tool not available" warning per tool per turn. The OAuth reconnect flow above keeps its own message.
        /// </summary>
        private static Task ReportMcpToolUnavailableAsync(IChatTurnErrorReporter chatTurnErrorReporter, string pluginName, int agentId, string agentName) =>
            chatTurnErrorReporter?.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, pluginName, agentId, agentName) ?? Task.CompletedTask;

        /// <summary>
        /// WI 7447/7575: the live <c>tools/list</c> entries that may be registered - on the tool's allowlist (Bug 7856:
        /// only while its permissions are managed; all functions when it has no enabled function) and
        /// not blocked for this agent (Ask stays registered: it is gated per call by the approval filter).
        /// </summary>
        internal static List<McpClientTool> FilterAllowedMcpFunctions(IEnumerable<McpClientTool> tools, int toolId, ToolPermissionPolicy permissionPolicy) =>
            (tools ?? [])
                .Where(t => permissionPolicy.GetMcpFunctionPermission(toolId, t.Name) != ToolPermissionValues.Block)
                .ToList();

        /// <summary>
        /// Notifies the chat user that an MCP OAuth2 tool needs to be reconnected, after
        /// <see cref="McpOAuthInteractiveConsentRequiredException"/> is caught above. Sends a live-only
        /// <see cref="ChatMessage"/> over <see cref="IChatNotificationService.SendMessageToChat"/> — mirroring
        /// the pattern <c>SemanticKernelAgentService.SendErrorMessageToChatAsync</c> already uses for error
        /// messages — never appended to <see cref="Contexts.ChatContext.CurrentChatHistory"/> or persisted to
        /// blob storage.
        /// </summary>
        /// <remarks>
        /// A plain clicked link can't work here: <c>/mcp/oauth/start</c> requires
        /// <c>AuthorizationMethod.AmaisJwtOnly</c> (a Bearer token), and a bare browser navigation has no way
        /// to carry an <c>Authorization</c> header. Instead this sends a <c>ChannelMessageType.Forms</c>
        /// message (the same generic <c>{ formType, data }</c> envelope <c>TextSuggestionPlugin</c> already
        /// uses) whose <see cref="CommonMetadataConsts.FormType"/> is <c>"McpOAuthReconnect"</c> — the
        /// wkflo_ui side renders a real button that fetches the (relative) start path with the widget's own
        /// auth headers, then opens the returned <c>authorizationUrl</c>.
        /// </remarks>
        /// <returns>
        /// <c>true</c> if a reconnect button could be built and was sent; <c>false</c> if a plain-text
        /// fallback was sent instead, because either no real WKFLO user identity is resolvable for the current
        /// caller, or this isn't a public-widget session (no
        /// <see cref="Contexts.AgentSharableContext.ShareId"/>). The
        /// <c>/api/v1/agent/{shareId}/tool/{id}/mcp/oauth/start</c> endpoint requires
        /// <c>AuthorizationMethod.AmaisJwtOnly</c> and would 401 an anonymous share-id-only session anyway, so
        /// a dead button is never sent.
        /// </returns>
        private static async Task<bool> SendMcpOAuthReconnectNotificationAsync(
            RunableTool runableTool,
            McpServerConfig serverConfig,
            Contexts.ChatContext chatContext,
            Contexts.AgentSharableContext agentSharableContext,
            Contexts.UserContext userContext,
            IChatNotificationService notificationService,
            int agentId,
            string agentName,
            IDateTimeProvider dateTimeProvider)
        {
            // Same resolvability check McpOAuthScopeResolver makes for PerUser tools — but applied
            // unconditionally here (regardless of this tool's own OAuthScope), because what matters for this
            // notification is whether *this chat session* has a real WKFLO identity that could complete the
            // AmaisJwtOnly-gated reconnect endpoint, not how this particular tool's token is scoped.
            var hasRealUserIdentity = Guid.TryParse(userContext?.Guid, out _);
            var shareId = agentSharableContext?.ShareId;

            var canBuildReconnectButton = hasRealUserIdentity
                && !string.IsNullOrWhiteSpace(shareId)
                && runableTool.McpOAuthConnectionId.HasValue;

            var metadata = new Dictionary<string, object>
            {
                { CommonMetadataConsts.RenderAsSystem, true }
            };

            string content;
            if (canBuildReconnectButton)
            {
                var message = $"The \"{serverConfig.Name}\" tool needs to be reconnected before it can be used again.";
                var startPath = $"/ai/api/v1/agent/{shareId}/tool/{runableTool.McpOAuthConnectionId.Value}/mcp/oauth/start";
                var update = new McpOAuthReconnectUpdate
                {
                    ToolName = serverConfig.Name,
                    StartPath = startPath,
                    Message = message
                };

                metadata[CommonMetadataConsts.FormType] = "McpOAuthReconnect";
                metadata[CommonMetadataConsts.Data] = JsonConvert.SerializeObject(update);

                content = message;
            }
            else
            {
                content = $"The \"{serverConfig.Name}\" tool needs to be reconnected — ask an admin to reconnect it from the dashboard.";
            }

            var chatMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "system",
                Content = content,
                ChannelMessageType = canBuildReconnectButton ? ChannelMessageType.Forms : ChannelMessageType.Metadata,
                Timestamp = dateTimeProvider.DateTimeOffsetUtcNow(),
                Metadata = metadata
            };

            await notificationService.SendMessageToChat(chatContext?.SessionId, chatMessage, agentId, agentName);

            return canBuildReconnectButton;
        }

        public static async Task AutoInvokePageDataAsync(this Kernel kernel, Contexts.ChatContext chatContext, ChatHistory chatHistory, IDateTimeProvider dateTimeProvider, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatContext.PagePath))
                return;

            var pagePlugin = kernel.Plugins
                .FirstOrDefault(p => p.Any(f => f.Name == PagePlugin.GetPageDataToolName));

            if (pagePlugin == null)
                return;

            var pageAlias = chatContext.PagePath.TrimStart('/');

            var sectionKey = $"PageData:{chatContext.PagePath}";
            var alreadyLoaded = TryGetSystemMessageSections(chatHistory, out _, out var pageDataSections) && pageDataSections.ContainsKey(sectionKey);

            if (alreadyLoaded)
            {
                return;
            }

            var result = await kernel.InvokeAsync(pagePlugin[PagePlugin.GetPageDataToolName], new KernelArguments { ["pageAlias"] = pageAlias }, cancellationToken);
            var pageData = result.GetValue<List<PluginResultItem>>();

            if (pageData != null && pageData.Count > 0)
            {
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.None }
                };
                UpsertSystemMessageSection(chatHistory, sectionKey, $"Page data (path: {chatContext.PagePath}): {JsonConvert.SerializeObject(pageData)}", metadata);
            }
        }

        public static async Task AutoInvokeTextFeatureDataAsync(this Kernel kernel, Contexts.ChatContext chatContext, ChatHistory chatHistory, IDateTimeProvider dateTimeProvider, CancellationToken cancellationToken = default)
        {
            if (chatContext.FeatureType != CommonFeatureMetadataConsts.TextFeatureType || string.IsNullOrEmpty(chatContext.FeatureId))
                return;

            if (!int.TryParse(chatContext.FeatureId, out var featureId))
                return;

            var textFeaturePlugin = kernel.Plugins
                .FirstOrDefault(p => p.Any(f => f.Name == TextFeaturePlugin.GetTextFeatureToolName));

            if (textFeaturePlugin == null)
                return;

            var sectionKey = $"TextFeature:{chatContext.FeatureId}";
            var alreadyLoaded = TryGetSystemMessageSections(chatHistory, out _, out var textFeatureSections) && textFeatureSections.ContainsKey(sectionKey);

            if (alreadyLoaded)
            {
                return;
            }

            var result = await kernel.InvokeAsync(textFeaturePlugin[TextFeaturePlugin.GetTextFeatureToolName], new KernelArguments { ["featureId"] = featureId }, cancellationToken);
            var featureData = result.GetValue<List<PluginResultItem>>();

            if (featureData != null && featureData.Count > 0)
            {
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.None }
                };
                UpsertSystemMessageSection(chatHistory, sectionKey, $"Text feature data (feature id: {chatContext.FeatureId}): {JsonConvert.SerializeObject(featureData)}", metadata);
            }
        }

        public static async Task AutoInvokeGalleryFeatureDataAsync(this Kernel kernel, Contexts.ChatContext chatContext, ChatHistory chatHistory, IDateTimeProvider dateTimeProvider, CancellationToken cancellationToken = default)
        {
            if (chatContext.FeatureType != CommonFeatureMetadataConsts.GalleryFeatureType || string.IsNullOrEmpty(chatContext.FeatureId))
                return;

            if (!int.TryParse(chatContext.FeatureId, out var featureId))
                return;

            var galleryFeaturePlugin = kernel.Plugins
                .FirstOrDefault(p => p.Any(f => f.Name == GalleryFeaturePlugin.GetGalleryFeatureToolName));

            if (galleryFeaturePlugin == null)
                return;

            var sectionKey = $"GalleryFeature:{chatContext.FeatureId}";
            var alreadyLoaded = TryGetSystemMessageSections(chatHistory, out _, out var galleryFeatureSections) && galleryFeatureSections.ContainsKey(sectionKey);

            if (alreadyLoaded)
            {
                return;
            }

            var result = await kernel.InvokeAsync(galleryFeaturePlugin[GalleryFeaturePlugin.GetGalleryFeatureToolName], new KernelArguments { ["featureId"] = featureId }, cancellationToken);
            var featureData = result.GetValue<List<PluginResultItem>>();

            if (featureData != null && featureData.Count > 0)
            {
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.None }
                };
                UpsertSystemMessageSection(chatHistory, sectionKey, $"Gallery feature data (feature id: {chatContext.FeatureId}): {JsonConvert.SerializeObject(featureData)}", metadata);
            }
        }

        public static async Task AutoInvokeAmaisKnowledgeAsync(this Kernel kernel, Contexts.ChatContext chatContext, ChatHistory chatHistory, string userQuery, IDateTimeProvider dateTimeProvider, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
                return;

            var knowledgePlugin = kernel.Plugins
                .FirstOrDefault(p => p.Any(f => f.Name == AmaisKnowledgePlugin.GetSchoolKnowledgeToolName));

            if (knowledgePlugin == null)
                return;

            var asOfUtc = dateTimeProvider.DateTimeOffsetUtcNow();
            var timeZone = ResolveTimeZone(chatContext?.TimeZone);

            // Last 1-2 prior user turns, so a vague follow-up ("what about the ones that already
            // happened") can be resolved against a prior turn's period. Same concatenation pattern as
            // AutoSelectAmaisPluginsAsync below, just limited to the most recent turns instead of the
            // whole history.
            var recentUserTurns = chatHistory
                .Where(m => m.Role == AuthorRole.User && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => m.Content)
                .TakeLast(2)
                .ToList();

            var (eventDateRangeStartUtc, eventDateRangeEndUtc, eventDateSortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(
                kernel, userQuery, recentUserTurns, asOfUtc, timeZone, cancellationToken);

            var result = await kernel.InvokeAsync(knowledgePlugin[AmaisKnowledgePlugin.GetSchoolKnowledgeToolName], new KernelArguments
            {
                ["message"] = userQuery,
                ["asOfUtc"] = asOfUtc,
                ["eventDateRangeStartUtc"] = eventDateRangeStartUtc,
                ["eventDateRangeEndUtc"] = eventDateRangeEndUtc,
                ["eventDateSortAscending"] = eventDateSortAscending
            }, cancellationToken);
            var knowledgeText = result.GetValue<string>();

            kernel.Plugins.Remove(knowledgePlugin);

            if (!string.IsNullOrWhiteSpace(knowledgeText))
            {
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.None }
                };

                // Framed as subordinate background so it doesn't get recited in place of the agent's
                // actual capabilities (which live in its Instructions, not here). The timezone-conversion
                // instruction is necessary because the retrieved passages carry raw UTC timestamps (e.g.
                // "Start date: 2026-09-14T17:30:00Z") verbatim from ingestion - EisDocument only exposes a
                // date-granularity EventEndDate structurally, not a start time, so there is no structured
                // field to convert on the model's behalf here. Without this note the model was observed
                // echoing the UTC digits as if they were already the school's local time.
                //
                // Deliberately does NOT precompute a single DST-aware label (e.g. "Pacific Daylight Time")
                // from asOfUtc/"now" and apply it uniformly: retrieved passages can carry timestamps from
                // any month (e.g. a January event referenced in a September conversation), and that event's
                // correct standard/daylight status depends on ITS OWN date, not today's. A single
                // now-computed label would mislabel (and could misconvert the offset of) any out-of-season
                // passage. Instead names the zone and defers the per-timestamp daylight-saving judgment to
                // the model, which has each passage's own date to work from.
                var timeZoneNote = timeZone.Id != "UTC"
                    ? $" All dates/times in the passages below are in UTC - the school's local timezone is {timeZone.Id}. For each individual date/time, convert it to that timezone's correct local time and label for THAT SPECIFIC DATE (daylight vs. standard time depends on the date being converted, not on today's date) before stating it to the user."
                    : "";

                var framedKnowledgeText = "Background knowledge (only relevant to specific factual questions — "
                    + "do not use this to describe your capabilities; defer to your tools/plugins list for that)."
                    + timeZoneNote + "\n"
                    + knowledgeText;

                UpsertSystemMessageSection(chatHistory, "Knowledge", framedKnowledgeText, metadata);
            }
        }

        /// <summary>
        /// Resolves the school's timezone from <see cref="Contexts.ChatContext.TimeZone"/> (a Windows
        /// timezone ID), falling back to UTC when the header is missing/empty or names an ID this runtime
        /// doesn't recognize - mirrors CMS's own <c>ConvertToSiteUtc</c> fallback so both sides fail open
        /// the same way instead of throwing.
        /// </summary>
        private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return TimeZoneInfo.Utc;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        public static async Task AutoSelectAmaisPluginsAsync(this Kernel kernel, ChatHistory chatHistory, string userQuery)
        {
            var userMessages = string.Join(". ", chatHistory
                .Where(m => m.Role == AuthorRole.User && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => m.Content));

            if (string.IsNullOrWhiteSpace(userMessages) && string.IsNullOrWhiteSpace(userQuery))
                return;

            var selectors = kernel.Data.Values
                .OfType<AmaisSkillSelectorPlugin>()
                .ToList();

            foreach (var selector in selectors)
            {
                await selector.SelectPluginsAsync(userMessages, userQuery);
            }
        }

        public static void ProcessResponse(this AgentResponseItem<StreamingChatMessageContent> response, IDateTimeProvider dateTimeProvider, ref long inputTokensCount, ref long outputTokensCount)
        {
            if (response.Message.Metadata == null)
            {
                response.Message.Metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() }
                };
            }
            else if (!response.Message.Metadata.ContainsKey(CommonMetadataConsts.Timestamp))
            {
                var updatedMetadata = new Dictionary<string, object>(response.Message.Metadata)
                {
                    { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() }
                };
                response.Message.Metadata = updatedMetadata;
            }

            if (response?.Message?.Metadata != null)
            {
                // OpenAI / Azure OpenAI — token usage comes through SK metadata
                var openAiUsage = response.Message.Metadata.Values
                    .OfType<OpenAI.Chat.ChatTokenUsage>()
                    .FirstOrDefault();

                if (openAiUsage != null)
                {
                    inputTokensCount += openAiUsage.InputTokenCount;
                    outputTokensCount += openAiUsage.OutputTokenCount;
                    return;
                }

                if (response.Message.Metadata.TryGetValue("Usage", out var usageObj) && usageObj != null)
                {
                    // Depending on your exact package wrapper, 'usageObj' might be a Microsoft.Extensions.AI.UsageDetails 
                    // or the native Anthropic/OpenAI usage object.
                    if (usageObj is Microsoft.Extensions.AI.UsageDetails usageDetails)
                    {
                        inputTokensCount += usageDetails?.InputTokenCount ?? 0;
                        outputTokensCount += usageDetails?.OutputTokenCount ?? 0;
                    }
                    else if (usageObj is Microsoft.Extensions.AI.UsageContent usageContent)
                    {
                        // UsageContent stores the token counts in the .Details property
                        var usage = usageContent.Details;

                        long inputTokens = (usage?.InputTokenCount ?? 0);
                        long outputTokens = (usage?.OutputTokenCount ?? 0);

                        if (inputTokens > 0 || outputTokens > 0)
                        {
                            inputTokensCount += inputTokens;
                            outputTokensCount += outputTokens;
                        }
                    }
                    else
                    {
                        int inputTokens = 0;
                        int outputTokens = 0;

                        // Case 1: It's an IDictionary (Common when metadata maps directly from JSON)
                        if (usageObj is System.Collections.IDictionary dict)
                        {
                            // Try OpenAI/Standard naming conventions
                            if (dict.Contains("InputTokenCount")) inputTokens = Convert.ToInt32(dict["InputTokenCount"]);
                            else if (dict.Contains("input_tokens")) inputTokens = Convert.ToInt32(dict["input_tokens"]);
                            else if (dict.Contains("prompt_tokens")) inputTokens = Convert.ToInt32(dict["prompt_tokens"]);

                            if (dict.Contains("OutputTokenCount")) outputTokens = Convert.ToInt32(dict["OutputTokenCount"]);
                            else if (dict.Contains("output_tokens")) outputTokens = Convert.ToInt32(dict["output_tokens"]);
                            else if (dict.Contains("completion_tokens")) outputTokens = Convert.ToInt32(dict["completion_tokens"]);
                        }
                        // Case 2: It's a System.Text.Json.JsonElement (Happens if passed as raw serialized JSON)
                        else if (usageObj is System.Text.Json.JsonElement json)
                        {
                            if (json.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                // Read Anthropic style or standard MEAI style properties
                                if (json.TryGetProperty("input_tokens", out var inProp) ||
                                    json.TryGetProperty("InputTokenCount", out inProp) ||
                                    json.TryGetProperty("prompt_tokens", out inProp))
                                {
                                    inputTokens = inProp.GetInt32();
                                }

                                if (json.TryGetProperty("output_tokens", out var outProp) ||
                                    json.TryGetProperty("OutputTokenCount", out outProp) ||
                                    json.TryGetProperty("completion_tokens", out outProp))
                                {
                                    outputTokens = outProp.GetInt32();
                                }
                            }
                        }
                        // Case 3: It's a strong-typed native class from the SDK (Fallback via reflection)
                        else if (usageObj != null)
                        {
                            var type = usageObj.GetType();

                            // Try getting input tokens
                            var inputProp = type.GetProperty("InputTokenCount")
                                            ?? type.GetProperty("input_tokens")
                                            ?? type.GetProperty("InputTokens");

                            // Try getting output tokens
                            var outputProp = type.GetProperty("OutputTokenCount")
                                             ?? type.GetProperty("output_tokens")
                                             ?? type.GetProperty("OutputTokens");

                            if (inputProp != null) inputTokens = Convert.ToInt32(inputProp.GetValue(usageObj));
                            if (outputProp != null) outputTokens = Convert.ToInt32(outputProp.GetValue(usageObj));
                        }

                        // Only process/print if we actually extracted valid token values
                        if (inputTokens > 0 || outputTokens > 0)
                        {
                            inputTokensCount += inputTokens;
                            outputTokensCount += outputTokens;
                        }
                    }
                }
            }
        }
    }
}
