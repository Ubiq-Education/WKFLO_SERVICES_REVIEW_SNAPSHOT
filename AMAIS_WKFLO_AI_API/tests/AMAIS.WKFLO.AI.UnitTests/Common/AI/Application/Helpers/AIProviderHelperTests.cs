using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.AI.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Reflection;

#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is experimental

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Helpers
{
    // Regression coverage for a live incident with two distinct failure modes, both hit in production:
    //   1. Azure's gpt-6-luna rejected function tools on /v1/chat/completions unless reasoning_effort was
    //      explicitly "none" - Semantic Kernel's own ReasoningEffort default is null (nothing sent), so
    //      Azure's own server-side default for reasoning models was what actually caused the 400.
    //   2. Setting ReasoningEffort to the STRING "none" then threw a client-side NotSupportedException
    //      from Semantic Kernel itself (ClientCore.GetEffortLevel) - its string-literal parser only
    //      recognizes "low"/"medium"/"high"/"minimal", not "none", even though the underlying OpenAI SDK
    //      type has a real None member. Passing the typed ChatReasoningEffortLevel.None value bypasses
    //      that stale parsing entirely.
    // BuildDefaultExecutionSettings_ExecutesAgainstSemanticKernelsOwnParser below invokes SK's actual
    // internal parser via reflection - a plain equality check on ReasoningEffort would NOT have caught
    // failure mode 2 (the value "looked right" but SK's own code rejected it at runtime).
    public class AIProviderHelperTests
    {
        private static TenantAgent BuildAgent(string provider, string model) => new()
        {
            AIModel = new AIModel { Provider = provider, Model = model }
        };

        [Theory]
        [InlineData("azure", "gpt-6-luna")]
        [InlineData("openai", "gpt-6-luna")]
        public void BuildDefaultExecutionSettings_AzureOrOpenAi_SetsReasoningEffortToNone(string provider, string model)
        {
            var agent = BuildAgent(provider, model);

            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            var openAiSettings = Assert.IsType<OpenAIPromptExecutionSettings>(settings);
            Assert.Equal(OpenAI.Chat.ChatReasoningEffortLevel.None, openAiSettings.ReasoningEffort);
        }

        [Theory]
        [InlineData("azure", "gpt-6-luna")]
        [InlineData("openai", "gpt-6-luna")]
        public void BuildDefaultExecutionSettings_ExecutesAgainstSemanticKernelsOwnParser(string provider, string model)
        {
            var agent = BuildAgent(provider, model);
            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            // Mirrors what Microsoft.SemanticKernel.Connectors.OpenAI.ClientCore.GetEffortLevel does
            // internally when building the actual chat completion request - the real regression guard,
            // since a naive equality check on ReasoningEffort wouldn't catch SK's own parser rejecting a
            // value that otherwise "looks right".
            var clientCoreType = typeof(OpenAIPromptExecutionSettings).Assembly
                .GetType("Microsoft.SemanticKernel.Connectors.OpenAI.ClientCore");
            var getEffortLevel = clientCoreType!.GetMethod("GetEffortLevel", BindingFlags.NonPublic | BindingFlags.Static);

            var exception = Record.Exception(() => getEffortLevel!.Invoke(null, new object[] { settings }));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData("anthropic")]
        [InlineData("google")]
        public void BuildDefaultExecutionSettings_NonOpenAiProvider_DoesNotSetReasoningEffort(string provider)
        {
            var agent = BuildAgent(provider, "some-model");

            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            Assert.IsNotType<OpenAIPromptExecutionSettings>(settings);
        }

        [Theory]
        [InlineData("azure", "gpt-4o")]
        [InlineData("azure", "gpt-4")]
        [InlineData("openai", "gpt-4o-mini")]
        public void BuildDefaultExecutionSettings_NonReasoningAzureOrOpenAiModel_DoesNotSetReasoningEffort(string provider, string model)
        {
            // Regression guard for a review finding on the initial version of this fix (flagged
            // independently by both the pr-reviewer agent and GitHub Copilot): gating on provider alone,
            // without also checking the model is reasoning-capable, would set reasoning_effort on every
            // Azure/OpenAI agent - including ordinary chat models that reject it as an unsupported
            // parameter, trading the narrow gpt-6-luna 400 for a much broader one.
            var agent = BuildAgent(provider, model);

            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            Assert.IsNotType<OpenAIPromptExecutionSettings>(settings);
        }

        [Fact]
        public void BuildDefaultExecutionSettings_AzureProviderRunningClaudeViaFoundry_DoesNotSetReasoningEffort()
        {
            // "azure" + a claude model means AIProviderHelper.GetProvider routes this through the
            // Anthropic Foundry IChatClient path (IsAnthropicModel), not AddAzureOpenAIChatCompletion -
            // there's no OpenAI-specific reasoning_effort concept on that path at all.
            var agent = BuildAgent("azure", "claude-sonnet-5");

            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            Assert.IsNotType<OpenAIPromptExecutionSettings>(settings);
        }

        [Fact]
        public void BuildDefaultExecutionSettings_NullAiModel_ReturnsPlainSettingsWithoutThrowing()
        {
            var agent = new TenantAgent { AIModel = null };

            var settings = AIProviderHelper.BuildDefaultExecutionSettings(agent);

            Assert.IsNotType<OpenAIPromptExecutionSettings>(settings);
        }

        private static readonly OpenAIProvider OpenAi = new() { ApiKey = "test-key", Model = "gpt-test" };

        // The kernel has its own service container. Without the host's logger factory, a bare AddLogging() there has
        // no providers, so SemanticKernelInvocationLogger's "Tool ... failed" error was written nowhere and a failed
        // tool showed in the logs only as WKFLOAI1113.
        [Fact]
        public void GetKernel_WithHostLoggerFactory_KernelLogsThroughIt()
        {
            var hostFactory = new RecordingLoggerFactory();

            var kernel = AIProviderHelper.GetKernel(OpenAi, null, null, new ChatContext(), loggerFactory: hostFactory);

            Assert.Same(hostFactory, kernel.Services.GetRequiredService<ILoggerFactory>());
            kernel.Services.GetRequiredService<ILogger<SemanticKernelInvocationLogger>>().LogError("Tool failed");
            Assert.Contains(typeof(SemanticKernelInvocationLogger).FullName, hostFactory.CreatedCategories);
        }

        [Fact]
        public void GetKernel_WithoutHostLoggerFactory_StillResolvesALogger()
        {
            var kernel = AIProviderHelper.GetKernel(OpenAi, null, null, new ChatContext());

            Assert.NotNull(kernel.Services.GetRequiredService<ILogger<SemanticKernelInvocationLogger>>());
        }

        private sealed class RecordingLoggerFactory : ILoggerFactory
        {
            public System.Collections.Generic.List<string> CreatedCategories { get; } = new();

            public ILogger CreateLogger(string categoryName)
            {
                CreatedCategories.Add(categoryName);
                return NullLogger.Instance;
            }

            public void AddProvider(ILoggerProvider provider) { }

            public void Dispose() { }
        }
    }
}
