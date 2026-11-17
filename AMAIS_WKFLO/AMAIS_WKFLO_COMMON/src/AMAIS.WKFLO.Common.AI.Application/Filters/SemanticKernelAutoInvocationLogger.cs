using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    public class SemanticKernelAutoInvocationLogger : IAutoFunctionInvocationFilter
    {
        private readonly ILogger<SemanticKernelAutoInvocationLogger> _logger;

        public SemanticKernelAutoInvocationLogger(ILogger<SemanticKernelAutoInvocationLogger> logger)
        {
            _logger = logger;
        }

        public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
        {
            var fn = context.Function;

            // Bug 8294: names and duration only. Tool arguments and results carry user and tenant content (messages,
            // document passages, external API bodies) and can be very large, so they are never logged.
            _logger.LogInformation("Invoking function {PluginName}.{FunctionName}", fn.PluginName, fn.Name);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            await next(context);

            sw.Stop();

            _logger.LogInformation("Finished invoking function {PluginName}.{FunctionName} in {Duration}ms", fn.PluginName, fn.Name, sw.ElapsedMilliseconds);
        }
    }
}
