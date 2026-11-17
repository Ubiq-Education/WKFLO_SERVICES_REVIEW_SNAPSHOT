using AMAIS.WKFLO.Common.AI.Application.Filters;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    public class SemanticKernelLargeToolOutputFilterTests
    {
        private const string LongResult = "0123456789";

        // Must return the plugin-attached function reference, not the pre-add one KernelFunctionFactory
        // hands back — AddFromFunctions wraps/copies it, and only the plugin-attached copy has PluginName
        // set, which is exactly what TryGetMaxLength parses. Invoking the pre-add reference directly (as an
        // earlier version of this test did) silently skips the filter's tool-lookup entirely, the same class
        // of gap the PR review on the OAuth work already caught once — matching real production invocation,
        // which always resolves functions through the kernel's plugin collection, not a bare reference.
        private static Kernel BuildKernelWithFunction(string pluginName, string functionName, string result, out KernelFunction function)
        {
            var kernel = Kernel.CreateBuilder().Build();
            var rawFunction = KernelFunctionFactory.CreateFromMethod(() => result, functionName);
            kernel.Plugins.AddFromFunctions(pluginName, [rawFunction]);
            function = kernel.Plugins[pluginName][functionName];
            return kernel;
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_ResultOverThreshold_TruncatesAndAppendsSuffix()
        {
            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", LongResult, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [42] = 5 }));

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("01234... [Truncated due to token limits]", result.ToString());
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_ResultUnderThreshold_LeavesResultUnchanged()
        {
            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", LongResult, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [42] = 100 }));

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(LongResult, result.ToString());
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_ToolHasNullThreshold_LeavesResultUnchanged()
        {
            // Null is the DB default (RunableTool.MaxToolOutputLength) — opt-out, not "cap at zero".
            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", LongResult, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [42] = null }));

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(LongResult, result.ToString());
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_ToolNotInDictionary_LeavesResultUnchanged()
        {
            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", LongResult, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [99] = 5 }));

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(LongResult, result.ToString());
        }

        [Theory]
        [InlineData("plugin_42_MCP", 42, true)]
        [InlineData("plugin_42_Integration_Amais_Knowledge", 42, true)] // toolType and suffix both contain underscores
        [InlineData("plugin_42_Chat_Classification", 42, true)]
        [InlineData(null, 0, false)]
        [InlineData("", 0, false)]
        [InlineData("not_a_plugin_name_shape_but_no_int_second", 0, false)]
        public void TryGetMaxLength_ParsesToolIdFromPluginNameRegardlessOfUnderscoresInTypeOrSuffix(string pluginName, int toolId, bool expectFound)
        {
            var filter = new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [toolId] = 123 });

            var found = filter.TryGetMaxLength(pluginName, out var maxLength);

            Assert.Equal(expectFound, found);
            if (expectFound)
                Assert.Equal(123, maxLength);
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_RunsBeforeAnOuterDIRegisteredFilterObservesTheResult()
        {
            // Regression guard for the ordering hazard this filter's remarks describe: a filter added to
            // Kernel.FunctionInvocationFilters BEFORE this one (simulating a DI-sourced filter added at
            // kernel-build time, e.g. SemanticKernelInvocationLogger) is outermost, so the code after ITS
            // OWN `await next(context)` must see the ALREADY-TRUNCATED result — proving truncation
            // completes before any outer filter's post-invocation logic runs, not after.
            string observedByOuterFilter = null;
            var recordingOuterFilter = new RecordingFilter(ctx => observedByOuterFilter = ctx.Result?.ToString());

            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", LongResult, out var function);
            kernel.FunctionInvocationFilters.Add(recordingOuterFilter); // added first => outermost, like a DI-sourced filter
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [42] = 5 })); // added after => innermost

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("01234... [Truncated due to token limits]", observedByOuterFilter);
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_GuardrailTaggedResult_IsNotTruncatedEvenIfOverThreshold()
        {
            // Regression guard: a result can reach this filter without the underlying tool ever running -
            // e.g. SemanticKernelToolCallLimitFilter (inner relative to this filter, exactly as registered in
            // SemanticKernelAgentService) short-circuits and sets its own "[GUARDRAIL] ..." instruction
            // instead of calling next() further. That instruction must reach the model intact, not get cut
            // into a fragment by a cap sized for real tool output.
            var kernel = BuildKernelWithFunction("plugin_42_MCP", "search_designs", "never reached", out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelLargeToolOutputFilter(new Dictionary<int, int?> { [42] = 5 })); // outer
            var guardrailText = "[GUARDRAIL] This instruction is far longer than the five character threshold configured above.";
            kernel.FunctionInvocationFilters.Add(new GuardrailSettingFilter(guardrailText)); // inner - short-circuits like SemanticKernelToolCallLimitFilter

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(guardrailText, result.ToString());
        }

        private sealed class RecordingFilter : IFunctionInvocationFilter
        {
            private readonly Action<FunctionInvocationContext> _onAfterNext;

            public RecordingFilter(Action<FunctionInvocationContext> onAfterNext) => _onAfterNext = onAfterNext;

            public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
            {
                await next(context);
                _onAfterNext(context);
            }
        }

        /// <summary>Test double standing in for SemanticKernelToolCallLimitFilter's real short-circuit behavior: sets a
        /// guardrail-tagged result and never calls next(), so the underlying function never runs.</summary>
        private sealed class GuardrailSettingFilter : IFunctionInvocationFilter
        {
            private readonly string _text;

            public GuardrailSettingFilter(string text) => _text = text;

            public Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
            {
                context.Result = new FunctionResult(context.Function, _text,
                    metadata: new Dictionary<string, object> { [GuardrailResultMetadata.IsGuardrailKey] = true });
                return Task.CompletedTask;
            }
        }
    }
}
