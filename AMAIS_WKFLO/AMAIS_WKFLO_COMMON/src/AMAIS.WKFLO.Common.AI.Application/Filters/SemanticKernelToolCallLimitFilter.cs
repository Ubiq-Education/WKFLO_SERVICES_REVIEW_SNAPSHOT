using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>
    /// Kernel function invocation filter that enforces a maximum number of tool calls per conversation turn.
    /// When the limit is reached, the filter returns a guardrail message to the LLM instructing it to
    /// stop calling tools and inform the user of the restriction.
    /// </summary>
    internal sealed class SemanticKernelToolCallLimitFilter : IFunctionInvocationFilter
    {
        private readonly Dictionary<string, int> _callCountPerPlugin = [];
        private readonly int _maxCalls;
        private static readonly HashSet<string> _amaisSuffixes = ["_Post", "_Page", "_Gallery"];

        internal SemanticKernelToolCallLimitFilter(int maxCalls) => _maxCalls = maxCalls;

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            var pluginName = context.Function.PluginName ?? string.Empty;

            // Task 8219: the document search counts too: a model could otherwise search the attached documents without end.
            if (_amaisSuffixes.Any(s => pluginName.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                || pluginName == ChatDocumentRetriever.SearchPluginName)
            {
                _callCountPerPlugin.TryGetValue(pluginName, out var count);
                _callCountPerPlugin[pluginName] = ++count;

                if (count > _maxCalls)
                {
                    context.Result = new FunctionResult(context.Function,
                        $"[GUARDRAIL] The tool call limit of {_maxCalls} per message has been reached for plugin '{pluginName}'. " +
                        $"Stop invoking tools immediately. " +
                        $"Inform the user in a clear and friendly way that you can only perform up to {_maxCalls} tool operations per message, " +
                        $"and suggest they break their request into smaller parts.",
                        metadata: new Dictionary<string, object> { [GuardrailResultMetadata.IsGuardrailKey] = true });
                    return;
                }
            }

            await next(context);
        }
    }
}
