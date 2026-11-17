using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>
    /// Caps a tool's output length before it re-enters the model's context — Canva's design/asset list
    /// tools are large enough to blow past a model's context limit outright (confirmed live: a single
    /// oversized Canva result pushed one prompt to 252k tokens against Anthropic's 200k cap).
    /// </summary>
    /// <remarks>
    /// Threshold is per-tool via <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.RunableTool.MaxToolOutputLength"/>
    /// (null = no cap for that tool — opt-in, not a global default), so it's registered directly on the
    /// kernel per turn by <c>SemanticKernelExtensions.AddWkflowToolsAsync</c> — not via global DI in
    /// <c>AIProviderHelper</c> — since a per-tool threshold needs the actual <c>RunableTool</c> rows for this
    /// agent's tools, which only exist at kernel-build time, not at DI-container-build time.
    ///
    /// Registering it this way also fixes an ordering hazard a global-DI registration would have: DI-sourced
    /// <see cref="IFunctionInvocationFilter"/>s (e.g. <see cref="SemanticKernelInvocationLogger"/>, which logs
    /// and pushes <c>context.Result</c> to the frontend over SignalR) are added to
    /// <see cref="Kernel.FunctionInvocationFilters"/> when the kernel is built; SK nests filters so the
    /// first-registered one is outermost, meaning code after its <c>await next(context)</c> runs *last* — a
    /// filter registered via DI would run its truncation only after every filter added later (like this one,
    /// added post-build in <c>AddWkflowToolsAsync</c>) has already read the untruncated result. Adding this
    /// filter directly to the already-built kernel makes it the innermost wrapper instead, so its truncation
    /// completes before any DI-sourced filter's own post-invocation code (including the SignalR push) ever
    /// sees <c>context.Result</c>.
    /// </remarks>
    public class SemanticKernelLargeToolOutputFilter : IFunctionInvocationFilter
    {
        private const string TruncationSuffix = "... [Truncated due to token limits]";

        private readonly IReadOnlyDictionary<int, int?> _maxOutputLengthsByToolId;

        public SemanticKernelLargeToolOutputFilter(IReadOnlyDictionary<int, int?> maxOutputLengthsByToolId)
        {
            _maxOutputLengthsByToolId = maxOutputLengthsByToolId ?? throw new ArgumentNullException(nameof(maxOutputLengthsByToolId));
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            await next(context);

            // A result can reach here without the underlying tool ever having run — e.g.
            // SemanticKernelToolCallLimitFilter (inner relative to this one) short-circuits and sets its own
            // "[GUARDRAIL] ..." instruction instead of calling next() further. That's a filter's own
            // message to the model, not tool output, and this filter's cap is sized for the latter — a low
            // MaxToolOutputLength could otherwise cut a guardrail instruction down into an unusable
            // fragment before the model ever sees it. See GuardrailResultMetadata's remarks.
            if (context.Result?.Metadata?.ContainsKey(GuardrailResultMetadata.IsGuardrailKey) == true)
                return;

            if (!TryGetMaxLength(context.Function?.PluginName, out var maxLength))
                return;

            var originalOutput = context.Result?.ToString();
            if (originalOutput == null || originalOutput.Length <= maxLength)
                return;

            var truncatedText = originalOutput.Substring(0, maxLength) + TruncationSuffix;
            context.Result = new FunctionResult(context.Function, truncatedText, context.Result.Culture, context.Result.Metadata);
        }

        /// <summary>
        /// Plugin names are always built as <c>"plugin_{toolId}_{toolType}"</c>, sometimes with a further
        /// suffix (e.g. Integration_Amais's <c>"..._Knowledge"</c>/<c>"..._TextSuggestion"</c> sub-plugins) —
        /// see <c>SemanticKernelExtensions.AddWkflowToolsAsync</c>'s <c>pluginName</c> construction. Split
        /// with a limit of 3 so a tool type containing its own underscores (e.g. "Integration_Amais",
        /// "Chat_Classification") doesn't shift where the id lands.
        /// </summary>
        internal bool TryGetMaxLength(string pluginName, out int maxLength)
        {
            maxLength = 0;

            if (string.IsNullOrEmpty(pluginName))
                return false;

            var parts = pluginName.Split('_', 3);
            if (parts.Length < 2 || !int.TryParse(parts[1], out var toolId))
                return false;

            if (!_maxOutputLengthsByToolId.TryGetValue(toolId, out var configured) || configured is not int max)
                return false;

            maxLength = max;
            return true;
        }
    }
}
