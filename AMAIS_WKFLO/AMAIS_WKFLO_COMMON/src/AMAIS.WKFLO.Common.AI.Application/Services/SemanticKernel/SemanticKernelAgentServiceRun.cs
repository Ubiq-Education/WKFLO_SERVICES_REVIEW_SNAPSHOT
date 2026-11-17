using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.SearchRunnableAgentVectorDataByEmail;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.ExceptionServices;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Runs a chat completion agent to process a user message within a chat session and returns the assistant's
        /// response along with token usage details.
        /// </summary>
        /// <remarks>This method streams the assistant's response tokens to the notification service as
        /// they are generated. The chat history is updated with both user and assistant messages, including optional
        /// reasoning if provided. The chat thread is deleted after processing is complete. Exceptions may be thrown if
        /// required parameters are null or if the operation is cancelled.</remarks>
        /// <param name="data">The data required to execute the chat completion agent's operations.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an AgentAnswer object with the
        /// assistant's response message, a unique message identifier, and token usage statistics.</returns>
        internal async Task<AgentAnswer> RunChatCompletionAgentAsync(RunChatData data, CancellationToken cancellationToken)
        {
            _chatContext.CurrentChatHistory = data.ChatHistoryAgentThread.ChatHistory;

            // If LTM is enabled, search for relevant information
            var vectorialResults = await _mediator.Send(new SearchRunnableAgentVectorDataByEmailQuery() { Agent = data.Agent, Message = data.Message }, cancellationToken);
            if (vectorialResults != null && vectorialResults.Any())
            {
                data.Prompt += "Relevant information about the user. Please take these next statments into account when replying back to the user:\n";
                foreach (var result in vectorialResults)
                {
                    data.Prompt += $"- {result}\n";
                }
            }

            // Inject PriorityQA predefined answers into the prompt so they are always honoured
            // regardless of FunctionChoiceBehavior or topic restrictions in the agent's system prompt
            var priorityQAInjection = BuildPriorityQAInjection(data.Agent, data.ToolPermissionPolicy);
            if (!string.IsNullOrEmpty(priorityQAInjection))
                data.Prompt += priorityQAInjection;

            // Inject ContactForm tool instructions so the AI knows exactly when to call the function
            var contactFormInjection = await BuildContactFormInjectionAsync(data.Agent, _tenantContext.TenantName, _chatContext.SubjectId, data.ToolPermissionPolicy);
            if (!string.IsNullOrEmpty(contactFormInjection))
                data.Prompt += contactFormInjection;

            // Hand-off prompt: catalog of allowlisted agents plus, on an @mention, a forced call. Returns the prompt
            // and the allowlisted target names (used below for the missed hand-off warning).
            var (agentEventPublisherInjection, handoffTargetNames) = await BuildAgentEventPublisherInjectionAsync(data.Agent, data.Message);
            if (!string.IsNullOrEmpty(agentEventPublisherInjection))
                data.Prompt += agentEventPublisherInjection;

            // Only build the Amais CMS page/feature integration context line when Amais skills are enabled
            // for this run — otherwise the model would be instructed to call Amais-only tools that aren't
            // actually available.
            var amaisContextLine = data.AmaisSkillsEnabled ? BuildAmaisContextLine(_chatContext, IsPageAnalyticsAvailable(_chatContext)) : string.Empty;

            // Inject the agent's actual tools/plugins so capability questions ("what can you help with?")
            // are answered from real configured tools instead of the generic system-prompt persona text.
            // Only built when Amais skills are enabled for this run — same gate as amaisContextLine above.
            var capabilitiesInjection = data.AmaisSkillsEnabled ? BuildAmaisAgentCapabilitiesInjection(data.Agent, _chatContext, data.ToolPermissionPolicy) : string.Empty;
            if (!string.IsNullOrEmpty(capabilitiesInjection))
                data.Prompt += capabilitiesInjection;

            // Get chat completion service
            var chatCompletionService = data.Kernel.GetRequiredService<IChatCompletionService>();

            // Sanitize agent name to comply with ChatCompletionAgent requirements
            var sanitizedAgentName = data.Agent.Name.SanitizeForAgentName();

            // Bug 8189 / Task 8219: the passages of the chat's large documents that match the question go into THIS turn's instructions
            // only (never the history, which is still as loaded: this turn's messages are added below). On a resume or contact-form replay
            // turn data.Message is not what the user typed, so the last typed message is the question.
            var attachedDocumentsContext = await BuildAttachedDocumentContextAsync(data.ChatHistoryAgentThread.ChatHistory, (data.Files ?? []).Select(f => f.Document),
                data.SessionId, data.Message, data.IsContactFormReplay || data.IsToolApprovalResume, data.Kernel, cancellationToken);

#pragma warning disable SKEXP0110
            // Create chat completion agent
            var chatCompletionAgent = new ChatCompletionAgent()
            {
                Name = sanitizedAgentName,
                Instructions = BuildChatCompletionAgentInstructions(_dateTimeProvider.DateTimeOffsetUtcNow(), data.Prompt, amaisContextLine, data.AssitantReasoning, data.IsMaisyAgent, attachedDocumentsContext),
                Kernel = data.Kernel,
                Arguments = new KernelArguments(AIProviderHelper.BuildDefaultExecutionSettings(data.Agent)),
                HistoryReducer = new SafeChatHistorySummarizationReducer(chatCompletionService, 20, 30)
            };
#pragma warning restore SKEXP0110

            // Token usage tracking
            long inputTokensCount = 0;
            long outputTokensCount = 0;

            // Reduce prior to invoking the agent
#pragma warning disable SKEXP0110
            await chatCompletionAgent.ReduceAsync(data.ChatHistoryAgentThread.ChatHistory, cancellationToken);
#pragma warning restore SKEXP0110 

            // Message Id
            var messageId = Guid.NewGuid().ToString();

            // Resolved once for this turn and reused for every streaming/typing notification below,
            // rather than re-querying per token.
            var chatbotIconUrl = await GetChatbotIconUrlAsync(data.Agent.Id, data.RootIsWidget, cancellationToken);
            _chatTurnErrorReporter?.SetCurrentAgent(new ChatTurnAgent(data.Agent.Id, data.Agent.Name, chatbotIconUrl));

            // Neither a ContactForm replay nor a tool approval resume records a new user message: the question
            // was already recorded by the original turn.
            var isContinuationTurn = data.IsContactFormReplay || data.IsToolApprovalResume;

            if (!isContinuationTurn)
            {
                // US 7974: one timestamp for the question and its attachments, so they stay together (in insertion
                // order) when the history endpoints sort by timestamp.
                var questionTimestamp = data.UserMessageTimestamp ?? _dateTimeProvider.DateTimeOffsetUtcNow();

                // Add new user message
                var metadataText = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, questionTimestamp },
                    { CommonMetadataConsts.MessageId, data.UserMessageId.ToString() }
                };

                if (!string.IsNullOrEmpty(data.TopicName))
                    metadataText[CommonMetadataConsts.TopicName] = data.TopicName;

                var messageContentText = new ChatMessageContentItemCollection { new TextContent(data.Message) };

                foreach (var file in data.Files ?? [])
                {
                    var fileName = file.FileName;
                    var fileData = file.FileData;
                    var fileSize = file.FileSize;

                    // Bug 8189 / Task 8219: a document the turn already processed (see ProcessFilesAsync): its short message, or its inline text.
                    if (file.Document != null || file.ExtractedText != null)
                    {
                        AddDocumentMessage(data.ChatHistoryAgentThread.ChatHistory, fileName, file.FileType ?? file.Document?.FileType, fileSize, file.Document, file.ExtractedText, questionTimestamp);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(fileData))
                    {
                        var mimeType = fileData.DetectMimeType();

                        if (mimeType.IsImageMimeType())
                        {
                            if (string.IsNullOrWhiteSpace(file.BlobUrl))
                            {
                                _logger.LogWarning("Image {FileName} has no BlobUrl — skipping from chat history to prevent bytes in persistent storage", fileName);
                            }
                            else
                            {
                                var (imageBytes, detectedMimeType) = fileData.ParseImageData();

                                var metadataImage = new Dictionary<string, object>
                                {
                                    { CommonMetadataConsts.Timestamp, questionTimestamp },
                                    { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() }
                                };

                                var messageContentImage = new ChatMessageContentItemCollection
                                {
                                    new TextContent(ChatImageLabel.ForUploadedImage(fileName, detectedMimeType)),
                                    new ImageContent(imageBytes, detectedMimeType)
                                    {
                                        Uri = new Uri(file.BlobUrl)
                                    }
                                };
                                metadataImage.Add(CommonMetadataConsts.FileName, fileName);
                                metadataImage.Add(CommonMetadataConsts.FileType, detectedMimeType);
                                metadataImage.Add(CommonMetadataConsts.FileSize, fileSize);

                                data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.User, messageContentImage, metadata: metadataImage);
                            }
                        }
                        else if (mimeType.IsDocumentMimeType())
                        {
                            var (documentBytes, detectedMimeType, extractedText) = fileData.ParseDocumentData();

                            if (!string.IsNullOrEmpty(extractedText))
                                AddDocumentMessage(data.ChatHistoryAgentThread.ChatHistory, fileName, detectedMimeType, fileSize, null, extractedText, questionTimestamp);
                        }
                        else
                        {
                            _logger.LogWarning("Unsupported file type uploaded: {MimeType}", mimeType);
                        }
                    }
                }

                data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.User, messageContentText, metadata: metadataText);
            }

            if (!string.IsNullOrWhiteSpace(data.AssitantReasoning))
            {
                // AuthorRole.Assistant, not System: Anthropic rejects a mid-conversation 'system' role
                // message unless it's the first entry or immediately follows a user/assistant turn, and
                // this can land back-to-back with the ImageAnalysis message below. RenderAsSystem tells
                // the chat widget to keep applying the System-style "thinking" treatment (💭 icon,
                // italic styling, "[Thinking]" label - see chat-messages.tsx in wkflo_ui) even though
                // the role sent to the LLM is now Assistant.
                // US 7974: the timestamp its streamed "thinking" bubble carried, so live = reloaded.
                var reasoningMetadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, data.AssitantReasoningTimestamp ?? _dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Thinking },
                    { CommonMetadataConsts.RenderAsSystem, true }
                };
                data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.Assistant, data.AssitantReasoning, metadata: reasoningMetadata);
            }

            if (!string.IsNullOrWhiteSpace(data.ImageAnalysis))
            {
                var imageAnalysisMetadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Thinking },
                    { CommonMetadataConsts.RenderAsSystem, true }
                };
                data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.Assistant, data.ImageAnalysis, metadata: imageAnalysisMetadata);
            }

            if (!string.IsNullOrWhiteSpace(data.TopicHandoffMessage))
            {
                var handoffInstruction = $"If you are unable to provide the information the user needs, or if the user asks to contact someone, use the following contact information: {data.TopicHandoffMessage}";
                SemanticKernelExtensions.UpsertSystemMessageSection(data.ChatHistoryAgentThread.ChatHistory, "TopicHandoff", handoffInstruction, null);
            }

            if (isContinuationTurn)
            {
                // WI 7575: the same ephemeral-cue mechanism also serves the tool approval resume turn, where
                // data.Message is the cue describing the decision (see ApplyToolApprovalDecisionAsync).
                //
                // This turn replays a question already recorded as an AuthorRole.User ChatHistory entry
                // during the original turn (the one that triggered the ContactForm tool) — do not add a
                // second, duplicate entry for the same text (that's why the "add new user message" block
                // above is skipped for a replay turn). The original turn's filler/registration reply
                // (e.g. "a registration form has been displayed...") must NOT be removed either — it's a
                // real part of the transcript the visitor actually saw, and Chat Logs must keep showing
                // all 3 messages (question, filler, real answer) in order. Deleting the filler to
                // "supersede" it was tried and rejected: it silently dropped a real message from Chat Logs.
                //
                // Invoking the chat completion agent below sends whatever's currently in ChatHistory to
                // the model as-is. Anthropic's Messages API requires the message list to end in a 'user'
                // turn for a completion request — see the AuthorRole.User-not-System-or-Assistant comment
                // in LoadSiblingAgentContextBySessionQueryHandler.BuildInjectedMessageAsync for this same
                // codebase's prior, confirmed encounters with Anthropic's strict role-ordering rules. So
                // add an ephemeral (never persisted or rendered — see
                // CommonMetadataConsts.IsEphemeralAgentContext / BuildPersistableChatHistory, the exact
                // mechanism LoadSiblingAgentContextBySessionQueryHandler already uses for this same class
                // of problem) User-role message carrying the replayed question text again: it makes the
                // completion request valid for every provider (this is the same shape every ordinary turn
                // already sends) and gives the model an explicit, well-formed cue that it can now answer.
                // Once BuildPersistableChatHistory strips this ephemeral message before publishing
                // ChatHistoryUpdatedDomainEvent, the persisted order is exactly: original User question,
                // original filler Assistant reply, new real Assistant reply — no duplicate visitor
                // message, filler intact.
                //
                // This block is placed here, AFTER the reasoning/image-analysis message additions above
                // (rather than immediately after the "skip adding a new user message" branch), so it is
                // provably always the LAST non-system message added during this turn — a replay turn that
                // also produces non-empty Chain-of-Thought reasoning or image-analysis output would
                // otherwise have those Assistant-authored messages land after the cue, putting the history
                // back into the exact "must end in a user turn" problem this cue exists to solve.
                var ephemeralReplayCueMetadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() },
                    { CommonMetadataConsts.IsEphemeralAgentContext, true }
                };
                data.ChatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.User, data.Message, metadata: ephemeralReplayCueMetadata);
            }

            // Invoke agent with user message and chat history
            var tokens = new List<string>();
            var assistantResponseList = new List<string>();

            // US 7974: the answer's timestamp, taken once when its first tokens are sent (when the chat shows the
            // bubble) and reused for every streaming update and for the persisted message, so live = reloaded.
            DateTimeOffset? messageTimestamp = null;
            var historyCountBeforeStreaming = data.ChatHistoryAgentThread.ChatHistory.Count;

            Exception stoppedBy = null;
            var streamingFailed = false;
            data.StreamingStarted = true;
            try
            {
                await foreach (AgentResponseItem<StreamingChatMessageContent> responseItem in chatCompletionAgent.InvokeStreamingAsync(data.ChatHistoryAgentThread, cancellationToken: cancellationToken))
                {
                    responseItem.ProcessResponse(_dateTimeProvider, ref inputTokensCount, ref outputTokensCount);
                    foreach (var item in responseItem.Message.Items)
                    {
#pragma warning disable SKEXP0110
                        switch (item)
                        {
                            case StreamingTextContent text:
                                tokens.Add(text.Text);
                                assistantResponseList.Add(text.Text);
                                _logger.LogInformation("Streaming token: {Token}", text.Text);
                                break;

                            case StreamingReasoningContent reasoning:
                                _logger.LogInformation("Streaming reasoning token: {Token} with metadata: {Metadata}", reasoning.Text, string.Join(", ", reasoning.Metadata?.Select(a => $"{a.Key}={a.Value}") ?? new List<string>()));
                                break;

                            case StreamingFunctionCallUpdateContent tool:
                                _logger.LogInformation("Streaming tool call update: {ToolName} with metadata: {Metadata}", tool.Name, string.Join(", ", tool.Metadata?.Select(a => $"{a.Key}={a.Value}") ?? new List<string>()));
                                break;
                        }
#pragma warning restore SKEXP0110

                        if (tokens.Count >= 5)
                        {
                            messageTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
                            await _notificationService.SendStreamingTokens(data.SessionId, messageId, messageTimestamp.Value, ReorganizeTokens(tokens), AuthorRole.Assistant, ChannelMessageType.Normal, false, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                            // Show typing indicator
                            await _notificationService.SendTypingIndicator(data.SessionId, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);
                            tokens = new List<string>();
                        }
                    }
                }
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                // US 7501: the user stopped this turn. Not an error: no error bubble. Some providers wrap the
                // cancellation, so any exception thrown once the token is cancelled is treated as the Stop.
                stoppedBy = ex;
            }
            catch (Exception ex)
            {
                // Any failure during streaming (provider rejection, network error, malformed request,
                // etc.). US 7959 / Task 7961: recorded as a code (classified from the exception, which only
                // goes to the logs) instead of sending a code-less bubble here; the turn's choke point
                // (BaseWorkflowActivity) reports it to the chat once, with the failing agent's typing off.
                _logger.LogError(ex, "Streaming chat completion failed for session {SessionId}", data.SessionId);
                RecordTurnFailure(ex, data.Agent, chatbotIconUrl, stopRequested: false);
                streamingFailed = true;

                // Discard any partial content streamed before the failure — the error is reported as a
                // separate message, so there is no partial answer left to finalize.
                tokens.Clear();
                assistantResponseList.Clear();
            }

            if (stoppedBy != null)
            {
                // US 7502: a stopped turn stores nothing of its own work (no question, no tool messages, no partial
                // text): its final save is the history as it was before the turn plus the "Stopped" marker. The tokens
                // used are still recorded. The cancellation then still reaches the workflow activity, which records the
                // turn as cancelled. (A real error, below, keeps its own handling.)
                await FinishStoppedTurnAsync(data, messageId, inputTokensCount, outputTokensCount, chatbotIconUrl);

                if (stoppedBy is OperationCanceledException)
                    ExceptionDispatchInfo.Capture(stoppedBy).Throw();

                throw new OperationCanceledException("The chat turn was stopped.", stoppedBy, cancellationToken);
            }

            messageTimestamp ??= _dateTimeProvider.DateTimeOffsetUtcNow();
            await _notificationService.SendStreamingTokens(data.SessionId, messageId, messageTimestamp.Value, ReorganizeTokens(tokens), AuthorRole.Assistant, ChannelMessageType.Normal, true, data.Agent.Id, data.Agent.Name, chatbotIconUrl);

            // Combine assistant responses
            var assistantResponse = string.Join(string.Empty, assistantResponseList);

            // Bug 8007: warn when the reply names an allowlisted agent but publish_agent_event was never called.
            // Ids and target names only: never the user's message or the reply text.
            if (!streamingFailed)
            {
                var missedHandoffs = FindAnnouncedButUncalledHandoffs(assistantResponse, handoffTargetNames, _chatContext.AgentEventPublishCalledThisTurn, _chatContext.ToolApprovalRequestedThisTurn);
                if (missedHandoffs.Count > 0)
                    _logger.LogWarning("Possible missed agent hand-off: agent {AgentId}, session {SessionId}, reply names {TargetAgents} but publish_agent_event was not called", data.Agent.Id, data.SessionId, string.Join(", ", missedHandoffs));
            }

            StampStreamedReply(data.ChatHistoryAgentThread.ChatHistory, historyCountBeforeStreaming, assistantResponse, messageId, messageTimestamp.Value);

            // US 7142 corner case: tag this turn's own assistant reply as a ContactForm filler — not a
            // real answer — whenever the ContactForm tool actually fired this turn (see
            // ChatContext.ContactFormRequestedThisTurn's own doc comment). Unconditional on top of the
            // block above (which only replaces metadata when the auto-added message lacks a Timestamp
            // key already) so this tag reaches the persisted message regardless of that branch. Read back
            // later by ConversationAnalysisWorkflowActivity.ExtractQuestionAndAnswer, which must skip past
            // this filler when looking for the real answer — otherwise the backfill's own resolution
            // judgment would be computed from the filler text, the same failure mode the near-immediate
            // MessageResolutionWorkflow path is guarded against just below.
            if (_chatContext.ContactFormRequestedThisTurn && data.ChatHistoryAgentThread.ChatHistory.Count > 0)
            {
                var lastAssistantMessage = data.ChatHistoryAgentThread.ChatHistory.Last();
                if (lastAssistantMessage.Role == AuthorRole.Assistant)
                {
                    var fillerMetadata = new Dictionary<string, object>(lastAssistantMessage.Metadata ?? new Dictionary<string, object>())
                    {
                        [CommonMetadataConsts.IsContactFormFiller] = true
                    };

                    data.ChatHistoryAgentThread.ChatHistory.RemoveAt(data.ChatHistoryAgentThread.ChatHistory.Count - 1);
                    data.ChatHistoryAgentThread.ChatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, lastAssistantMessage.Content, metadata: fillerMetadata));
                }
            }

            // WI 7575: the turn stopped to wait for a tool approval - answer any call left without a result and end
            // with the "needs approval" notice. The approval card itself is one of the post messages below.
            if (data.ToolApprovalFilter?.RequestedApproval is { } requestedApproval)
            {
                assistantResponse = FinalizeToolApprovalTurn(data.ChatHistoryAgentThread.ChatHistory, requestedApproval, assistantResponse,
                    messageId, messageTimestamp.Value);
            }

            // Process assistant post messages that other part of the system like plugin could have added
            var postMessagesSent = _chatContext.AssistantPostMessagesToBeProcessed.Count > 0;
            await ProcessAssistantPostMessagesAsync(data.ChatHistoryAgentThread.ChatHistory, data.Agent, data.RootIsWidget);

            // US 7959 / Task 7961: the user now has the whole answer; a later failure (e.g. saving the history) is not
            // reported to the chat as a failed turn.
            if (!streamingFailed && (!string.IsNullOrWhiteSpace(assistantResponse) || postMessagesSent))
                _chatTurnFailureState.MarkAnswerDelivered();

            // Create AgentAnswer with token usage and other details
            var agentAnswer = BuildAgentAnswer(data, messageId, assistantResponse, inputTokensCount, outputTokensCount);

            // US 7502: the answer was fully streamed, so it is recorded and saved even when a Stop lands right now: the
            // user already saw it. Hence CancellationToken.None (a cancelled token would drop the save).
            await _mediator.Publish(new AgentTokensUsedDomainEvent(agentAnswer), CancellationToken.None);

            // US 7502: from here this turn's history is saved; a Stop caught by the caller must not save it again.
            data.TurnSaved = true;

            // Publish chat history updated event for RunnableAgents to trigger any updates in the UI or other parts of the system
            if (data.Agent is RunnableAgent runnableAgent)
            {
                // US 7078: never persist ephemeral sibling-agent context — see BuildPersistableChatHistory.
                // US 7502: the "Stopped" markers detached from the model's history are put back.
                await _mediator.Publish(new ChatHistoryUpdatedDomainEvent
                (
                    runnableAgent,
                    data.SessionId,
                    data.ChatName,
                    data.CreatedAt,
                    _dateTimeProvider.DateTimeOffsetUtcNow(),
                    BuildPersistableChatHistory(data.ChatHistoryAgentThread.ChatHistory, data.DetachedStoppedMarkers),
                    chatCompletionService
                ), CancellationToken.None);
            }

            // US 7142 corner case: when this turn's response is just the ContactForm tool's
            // registration-required filler (see ChatContext.ContactFormRequestedThisTurn's own doc
            // comment), record this question's MessageId so the eventual replay turn (once the visitor
            // actually submits the form) can reuse it — see RunTextOptions.OriginalMessageId. Best-effort,
            // same "never break the main chat flow" guarantee as the resolution scheduling below.
            if (_chatContext.ContactFormRequestedThisTurn && _contactFormReplayStateService != null)
            {
                try
                {
                    await _contactFormReplayStateService.RecordDeferredQuestionAsync(data.SessionId, data.UserMessageId, cancellationToken);
                }
                catch (Exception)
                {
                    // Swallow — a missing deferred-question record just means the eventual replay falls
                    // back to a freshly-generated MessageId (see RunTextOptions.OriginalMessageId).
                }
            }

            // Schedule the near-immediate, message-level resolution judgment (US 7142) right where the
            // conversation-level analysis workflow is already scheduled downstream via
            // ChatHistoryUpdatedDomainEvent — same "never break the response to the user" guarantee: a
            // failure to schedule must not surface here. data.Message/assistantResponse/data.UserMessageId
            // are already the exact question/answer/message-id for this turn, no ChatHistory re-scan needed.
            // messageId (this turn's own assistant answer id, generated above and already used as the
            // persisted assistant ChatMessageContent's own MessageId metadata) is threaded through as
            // AnswerMessageId so a later rating — always given on the answer — can look the classification
            // row up directly, instead of re-deriving the question from chat history.
            //
            // Skipped entirely when ContactFormRequestedThisTurn is set (see above) — this turn's "answer"
            // is just the tool's filler text, not a real response to the user's question, so judging it
            // would report the wrong Response to DXP and permanently mark the question unresolved even
            // once the real answer arrives later via the replay turn. The replay turn itself schedules its
            // own MessageResolutionWorkflow normally (ContactFormRequestedThisTurn is never set on a replay
            // call, since the ContactForm tool isn't re-invoked once info is already collected).
            //
            // Also skipped when assistantResponse is blank (e.g. the streaming failure path above clears it
            // and reports a separate error message instead) — judging a blank answer would run the LLM over
            // an empty exchange and, per ParseIsResolved's own unparseable-output default, permanently stamp
            // IsResolved = true on a turn that never produced a real answer. Same "never fabricate a
            // resolution off a non-answer" rule as the ContactForm case above.
            if (_messageResolutionWorkflowScheduler != null
                && !_chatContext.ContactFormRequestedThisTurn
                && !_chatContext.ToolApprovalRequestedThisTurn
                && !string.IsNullOrWhiteSpace(assistantResponse))
            {
                try
                {
                    var instanceId = $"msg-resolution-{data.UserMessageId}";
                    var resolutionInput = new MessageResolutionWorkflowInput
                    {
                        TenantName = _tenantContext.TenantName,
                        AgentId = data.Agent.Id,
                        SessionId = data.SessionId,
                        UserId = _userContext.Guid,
                        MessageId = data.UserMessageId,
                        Question = data.Message,
                        Answer = assistantResponse,
                        AnswerMessageId = Guid.Parse(messageId)
                    };

                    await _messageResolutionWorkflowScheduler.ScheduleAsync(instanceId, resolutionInput);
                }
                catch (Exception)
                {
                    // Swallow — resolution scheduling failure must not break the main chat flow.
                }
            }

            // Delete the thread when it is no longer needed
            await data.ChatHistoryAgentThread.DeleteAsync();

            return agentAnswer;
        }

        /// <summary>
        /// Adds a document to the history as one user message (same timestamp as the question): the short message of an indexed
        /// document (Bug 8189 / Task 8219), or, for a short document, its whole text. The text item carries the file card metadata.
        /// </summary>
        private static void AddDocumentMessage(ChatHistory chatHistory, string fileName, string fileType, int fileSize, AttachedDocumentInfo document,
            string inlineText, DateTimeOffset timestamp)
        {
            var item = document != null
                ? new TextContent(AttachedDocumentMessage.BuildText(document), metadata: AttachedDocumentMessage.BuildItemMetadata(document))
                : new TextContent($"{AttachedDocumentMessage.InlinePrefix}{inlineText}", metadata: new Dictionary<string, object>
                {
                    { CommonMetadataConsts.FileName, fileName },
                    { CommonMetadataConsts.FileType, fileType },
                    { CommonMetadataConsts.FileSize, fileSize }
                });

            chatHistory.AddMessage(AuthorRole.User, new ChatMessageContentItemCollection { item }, metadata: new Dictionary<string, object>
            {
                { CommonMetadataConsts.Timestamp, timestamp },
                { CommonMetadataConsts.MessageId, Guid.NewGuid().ToString() }
            });
        }

        /// <summary>
        /// Stamps this turn's assistant reply, added by the agent thread while streaming, with the content, message id
        /// and timestamp the chat was streamed. US 7974: Timestamp and MessageId are always overwritten, so the persisted
        /// reply matches the streamed bubble even when the reply already carries a Timestamp of its own. Messages added
        /// before the stream (the question, thinking, image analysis) are never touched, even when the stream added none.
        /// </summary>
        /// <param name="historyCountBeforeStreaming">The history's message count right before streaming started.</param>
        internal static void StampStreamedReply(ChatHistory chatHistory, int historyCountBeforeStreaming, string content,
            string messageId, DateTimeOffset messageTimestamp)
        {
            if (chatHistory.Count <= historyCountBeforeStreaming)
                return;

            var lastMessage = chatHistory[^1];
            if (lastMessage.Role != AuthorRole.Assistant)
                return;

            var metadata = new Dictionary<string, object>(lastMessage.Metadata ?? new Dictionary<string, object>())
            {
                [CommonMetadataConsts.Timestamp] = messageTimestamp,
                [CommonMetadataConsts.MessageId] = messageId
            };

            chatHistory.RemoveAt(chatHistory.Count - 1);
            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, content, metadata: metadata));
        }

        private static AgentAnswer BuildAgentAnswer(RunChatData data, string messageId, string message, long inputTokensCount, long outputTokensCount) => new()
        {
            MessageId = messageId,
            Message = message,
            TokenUsage = new AITokenUsage
            {
                InputTokenCount = inputTokensCount,
                OutputTokenCount = outputTokensCount
            },
            Provider = data.Agent.AIModel.Provider,
            Model = data.Agent.AIModel.Model,
            AgentType = data.AgentTagNames != null && data.AgentTagNames.Any() ? string.Join("_", data.AgentTagNames) : data.Agent.Name.Replace(' ', '_'),
            AgentTags = data.AgentTagNames,
            AgentId = data.Agent.Id,
            AIModelId = data.Agent.AIModel.Id
        };
    }
}
