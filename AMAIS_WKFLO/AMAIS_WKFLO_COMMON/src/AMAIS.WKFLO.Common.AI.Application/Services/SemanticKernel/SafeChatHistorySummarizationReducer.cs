using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    /// <summary>
    /// Drop-in replacement for Microsoft.SemanticKernel.ChatCompletion.ChatHistorySummarizationReducer.
    /// Ports its message-selection algorithm (see ChatHistoryReducerExtensions in the SK source), but sends
    /// the summarization instruction as an AuthorRole.User message instead of AuthorRole.System: the built-in
    /// reducer always appends an AuthorRole.System message as the last entry of its internal summarization
    /// request, regardless of what role precedes it, which violates Anthropic's rule that a mid-conversation
    /// system message must follow a user message (or a server-tool-result-ending assistant turn) and is only
    /// accepted on a handful of models (Opus 5/4.8, Fable 5, Mythos 5) - Sonnet and others reject it with a 400.
    /// A trailing User message has no such restriction on any provider, since Anthropic (and others) always
    /// accept a user turn after an assistant turn, and simply merge consecutive user turns rather than reject them.
    /// </summary>
    public class SafeChatHistorySummarizationReducer : IChatHistoryReducer
    {
        public const string SummaryMetadataKey = "__summary__";

        public const string DefaultSummarizationPrompt =
            """
            Provide a concise and complete summarization of the entire dialog that does not exceed 5 sentences

            This summary must always:
            - Consider both user and assistant interactions
            - Maintain continuity for the purpose of further dialog
            - Include details from any existing summary
            - Focus on the most significant aspects of the dialog

            This summary must never:
            - Critique, correct, interpret, presume, or assume
            - Identify faults, mistakes, misunderstanding, or correctness
            - Analyze what has not occurred
            - Exclude details from any existing summary
            """;

        public string SummarizationInstructions { get; init; } = DefaultSummarizationPrompt;
        public bool FailOnError { get; init; } = true;
        public bool UseSingleSummary { get; init; } = true;

        private readonly IChatCompletionService _service;
        private readonly int _targetCount;
        private readonly int _thresholdCount;

        public SafeChatHistorySummarizationReducer(IChatCompletionService service, int targetCount, int? thresholdCount = null)
        {
            ArgumentNullException.ThrowIfNull(service);
            if (targetCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetCount), "Target message count must be greater than zero.");
            if (thresholdCount.HasValue && thresholdCount <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdCount), "The reduction threshold length must be greater than zero.");

            _service = service;
            _targetCount = targetCount;
            _thresholdCount = thresholdCount ?? 0;
        }

        public async Task<IEnumerable<ChatMessageContent>> ReduceAsync(IReadOnlyList<ChatMessageContent> chatHistory, CancellationToken cancellationToken = default)
        {
            var systemMessage = chatHistory.FirstOrDefault(l => l.Role == AuthorRole.System);

            var insertionPoint = LocateSummarizationBoundary(chatHistory, SummaryMetadataKey);

            var truncationIndex = LocateSafeReductionIndex(chatHistory, _targetCount, _thresholdCount, insertionPoint, hasSystemMessage: systemMessage is not null);

            if (truncationIndex < 0) return null;

            var summarizedHistory = Extract(
                chatHistory,
                UseSingleSummary ? 0 : insertionPoint,
                truncationIndex,
                filter: m => m.Items.Any(i => i is FunctionCallContent or FunctionResultContent));

            try
            {
                var summarizationRequest = new ChatHistory(summarizedHistory.ToList());
                summarizationRequest.AddMessage(AuthorRole.User, $"<summarization-instructions>\n{SummarizationInstructions}\n</summarization-instructions>");

                var summaryMessage = await _service.GetChatMessageContentAsync(summarizationRequest, cancellationToken: cancellationToken).ConfigureAwait(false);
                summaryMessage.Metadata = new Dictionary<string, object> { { SummaryMetadataKey, true } };

                return AssembleSummarizedHistory(chatHistory, summaryMessage, systemMessage, insertionPoint, truncationIndex);
            }
            catch
            {
                if (FailOnError) throw;
                return null;
            }
        }

        private IEnumerable<ChatMessageContent> AssembleSummarizedHistory(
            IReadOnlyList<ChatMessageContent> chatHistory,
            ChatMessageContent summaryMessage,
            ChatMessageContent systemMessage,
            int insertionPoint,
            int truncationIndex)
        {
            if (systemMessage is not null)
            {
                yield return systemMessage;
            }

            if (insertionPoint > 0 && !UseSingleSummary)
            {
                for (var index = 0; index <= insertionPoint - 1; ++index)
                {
                    yield return chatHistory[index];
                }
            }

            if (summaryMessage is not null)
            {
                yield return summaryMessage;
            }

            for (var index = truncationIndex; index < chatHistory.Count; ++index)
            {
                yield return chatHistory[index];
            }
        }

        // Ported from Microsoft.SemanticKernel.ChatCompletion.ChatHistoryReducerExtensions (internal in SK.Core,
        // so unreachable from this assembly) - identifies the first message that is not already a summary.
        private static int LocateSummarizationBoundary(IReadOnlyList<ChatMessageContent> chatHistory, string summaryKey)
        {
            for (var index = 0; index < chatHistory.Count; ++index)
            {
                var message = chatHistory[index];
                if (!message.Metadata?.ContainsKey(summaryKey) ?? true)
                {
                    return index;
                }
            }
            return chatHistory.Count;
        }

        // Ported from ChatHistoryReducerExtensions.LocateSafeReductionIndex, with one correctness fix: the
        // original computes messageIndex = chatHistory.Count - targetCount without clamping, which indexes one
        // past the end (chatHistory[chatHistory.Count]) and throws ArgumentOutOfRangeException whenever
        // hasSystemMessage's implicit "targetCount -= 1" drives the effective targetCount down to 0 (e.g. a
        // caller-supplied targetCount of 1 combined with a pinned system message). Clamped below.
        private static int LocateSafeReductionIndex(
            IReadOnlyList<ChatMessageContent> chatHistory,
            int targetCount,
            int? thresholdCount,
            int offsetCount,
            bool hasSystemMessage)
        {
            targetCount -= hasSystemMessage ? 1 : 0;

            var thresholdIndex = chatHistory.Count - (thresholdCount ?? 0) - targetCount;

            if (thresholdIndex <= offsetCount)
            {
                return -1;
            }

            var messageIndex = Math.Min(chatHistory.Count - targetCount, chatHistory.Count - 1);

            while (messageIndex >= 0)
            {
                if (!chatHistory[messageIndex].Items.Any(i => i is FunctionCallContent or FunctionResultContent))
                {
                    break;
                }
                --messageIndex;
            }

            var targetIndex = messageIndex;

            while (messageIndex >= thresholdIndex)
            {
                if (chatHistory[messageIndex].Role == AuthorRole.User)
                {
                    return messageIndex;
                }
                --messageIndex;
            }

            return targetIndex;
        }

        // Ported from ChatHistoryReducerExtensions.Extract.
        private static IEnumerable<ChatMessageContent> Extract(
            IReadOnlyList<ChatMessageContent> chatHistory,
            int startIndex,
            int? finalIndex = null,
            Func<ChatMessageContent, bool> filter = null)
        {
            var maxIndex = chatHistory.Count - 1;
            if (startIndex > maxIndex)
            {
                yield break;
            }

            finalIndex ??= maxIndex;
            finalIndex = Math.Min(finalIndex.Value, maxIndex);

            for (var index = startIndex; index <= finalIndex; ++index)
            {
                if (filter?.Invoke(chatHistory[index]) ?? false)
                {
                    continue;
                }
                yield return chatHistory[index];
            }
        }
    }
}
