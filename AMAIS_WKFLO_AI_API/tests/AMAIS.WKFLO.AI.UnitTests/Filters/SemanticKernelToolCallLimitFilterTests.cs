using AMAIS.WKFLO.Common.AI.Application.Filters;
using Microsoft.SemanticKernel;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    public class SemanticKernelToolCallLimitFilterTests
    {
        private static Kernel BuildKernelWithFunction(string pluginName, string functionName, Func<string> body, out KernelFunction function)
        {
            var kernel = Kernel.CreateBuilder().Build();
            var rawFunction = KernelFunctionFactory.CreateFromMethod(body, functionName);
            kernel.Plugins.AddFromFunctions(pluginName, [rawFunction]);
            function = kernel.Plugins[pluginName][functionName];
            return kernel;
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_UnderLimit_CallsTheRealFunctionEachTime()
        {
            var callCount = 0;
            var kernel = BuildKernelWithFunction("plugin_1_Integration_Amais_Post", "get_post", () => { callCount++; return "ok"; }, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(3));

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_OverLimit_ReturnsGuardrailInsteadOfCallingTheFunction()
        {
            var callCount = 0;
            var kernel = BuildKernelWithFunction("plugin_1_Integration_Amais_Post", "get_post", () => { callCount++; return "ok"; }, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(2));

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            var thirdResult = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, callCount); // the 3rd call never reached the real function
            Assert.Contains("[GUARDRAIL]", thirdResult.ToString());
            Assert.Contains("2", thirdResult.ToString());
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_GuardrailResult_CarriesGuardrailMetadataFlag()
        {
            // Confirmed against SemanticKernelLargeToolOutputFilter's own remarks: this flag is what tells
            // any later filter that also transforms context.Result (e.g. a length-based truncation cap)
            // that this is a synthetic instruction, not real tool output, and must be left alone.
            var kernel = BuildKernelWithFunction("plugin_1_Integration_Amais_Page", "get_page", () => "ok", out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(0));

            var result = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Metadata?.ContainsKey(GuardrailResultMetadata.IsGuardrailKey));
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_TheAttachedDocumentsSearch_CountsTowardsTheLimit()
        {
            var callCount = 0;
            var kernel = BuildKernelWithFunction("AttachedDocuments", "search_attached_documents", () => { callCount++; return "passages"; }, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(1));

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            var second = await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, callCount);
            Assert.Contains("[GUARDRAIL]", second.ToString());
        }

        [Fact]
        public async Task OnFunctionInvocationAsync_PluginWithoutAQualifyingSuffix_NeverLimited()
        {
            var callCount = 0;
            var kernel = BuildKernelWithFunction("plugin_1_MCP", "search", () => { callCount++; return "ok"; }, out var function);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelToolCallLimitFilter(1));

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);
            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, callCount);
        }
    }
}
