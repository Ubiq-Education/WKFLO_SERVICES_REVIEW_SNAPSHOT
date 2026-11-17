using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI.Providers;
using Anthropic;
using Anthropic.Core;
using Anthropic.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AMAIS.WKFLO.Common.AI.Application.Helpers
{
    public static class AIProviderHelper
    {
        // Shared by GetProvider (routing) and BuildDefaultExecutionSettings (execution settings) so the
        // heuristic only has to be updated in one place if it ever changes.
        private static bool IsAnthropicModel(string model) =>
            model?.Contains("claude", StringComparison.OrdinalIgnoreCase) == true;

        // Name-based stopgap until reasoning-capability becomes an explicit, UI-configurable AIModel field.
        // Only these families are known to require (and accept) an explicit reasoning_effort value.
        private static readonly string[] ReasoningCapableModelPrefixes = { "o1", "o3", "o4", "gpt-5", "gpt-6" };

        private static bool IsReasoningCapableModel(string model) =>
            !string.IsNullOrEmpty(model)
            && ReasoningCapableModelPrefixes.Any(prefix => model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        public static AIBaseProvider GetProvider(Agent agent)
        {
            return (Enum.TryParse<ProviderEnum>(agent.AIModel.Provider, out var providerEnum) ? providerEnum : default) switch
            {
                ProviderEnum.azure => new AzureOpenAIProvider()
                {
                    Model = agent.AIModel.Model,
                    DeploymentName = agent.AIModel.DeploymentName,
                    Endpoint = agent.AIModel.Endpoint,
                    ApiVersion = agent.AIModel.ApiVersion,
                    ApiKey = agent.AIModel.Secret?.ApiKey,
                    UseManagedIdentity = Enum.TryParse<AuthModeEnum>(agent.AIModel.AuthMode, true, out var authMode) && authMode == AuthModeEnum.managedIdentity,
                    IsAnthropicModel = IsAnthropicModel(agent.AIModel.Model)
                },
                ProviderEnum.openai => new OpenAIProvider()
                {
                    Model = agent.AIModel.Model,
                    ApiKey = agent.AIModel.Secret.ApiKey
                },
                ProviderEnum.anthropic => new AnthropicAIProvider()
                {
                    Model = agent.AIModel.Model,
                    ApiKey = agent.AIModel.Secret.ApiKey
                },
                ProviderEnum.google => new GoogleAIProvider()
                {
                    Model = agent.AIModel.Model,
                    ApiKey = agent.AIModel.Secret.ApiKey
                },
                _ => null,
            };
        }

        // Some newer Azure/OpenAI reasoning-capable models (e.g. gpt-6-luna) reject function tools on
        // /v1/chat/completions unless reasoning_effort is explicitly set to "none" - Azure applies its own
        // non-"none" default server-side whenever the client omits the parameter entirely, and Semantic
        // Kernel's own OpenAIPromptExecutionSettings.ReasoningEffort default is null (nothing sent), so
        // leaving it unset lets that incompatible Azure default apply. Scoped to azure/openai only - an
        // "azure" provider running an Anthropic model via Azure AI Foundry (IsAnthropicModel, see
        // GetProvider above) goes through a completely different IChatClient path with no OpenAI-specific
        // execution settings concept, so it's excluded here even though its Provider string is "azure".
        //
        // Also scoped to IsReasoningCapableModel: non-reasoning deployments (GPT-4, GPT-4o, etc.) reject
        // reasoning_effort as an unsupported parameter, so setting it unconditionally on every Azure/OpenAI
        // agent would trade this narrow fix for a much broader one (caught in review by both pr-reviewer
        // and Copilot on the initial version of this change).
        //
        // Must be the typed ChatReasoningEffortLevel.None value, NOT the string "none": empirically
        // verified (via SK 1.80.0's ClientCore.GetEffortLevel) that its string-literal parser only
        // recognizes "low"/"medium"/"high"/"minimal" and throws NotSupportedException on "none" even
        // though the underlying OpenAI SDK type itself has a None member - passing the enum-like value
        // directly bypasses that stale string parsing entirely.
#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is experimental
        public static PromptExecutionSettings BuildDefaultExecutionSettings(Agent agent)
        {
            var settings = new PromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };

            if (!Enum.TryParse<ProviderEnum>(agent?.AIModel?.Provider, out var providerEnum))
                return settings;

            bool isAnthropicOnAzure = providerEnum == ProviderEnum.azure && IsAnthropicModel(agent.AIModel.Model);

            if (isAnthropicOnAzure
                || (providerEnum != ProviderEnum.azure && providerEnum != ProviderEnum.openai)
                || !IsReasoningCapableModel(agent.AIModel.Model))
                return settings;

            return new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                ReasoningEffort = OpenAI.Chat.ChatReasoningEffortLevel.None
            };
        }
#pragma warning restore OPENAI001

        public static Kernel GetKernel(AIBaseProvider provider, IChatNotificationService chatNotificationService, IDateTimeProvider dateTimeProvider, ChatContext chatContext,
            IMcpClientService mcpService = null, IBlobStorageProvider blobStorageProvider = null, IEisKnowledgeProvider eisKnowledgeService = null,
            UserContext userContext = null, TenantContext tenantContext = null, IChatTurnErrorReporter chatTurnErrorReporter = null,
            ILoggerFactory loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(provider);

            IKernelBuilder builder = Kernel.CreateBuilder();

            if (provider is AzureOpenAIProvider azureProvider)
            {
                builder = Kernel.CreateBuilder();

                if (azureProvider.IsAnthropicModel)
                {
                    IChatClient CreateChatClient(IServiceProvider sp)
                    {
                        var foundryResource = ExtractFoundryResourceName(azureProvider.Endpoint);
                        IChatClient innerChatClient = azureProvider.UseManagedIdentity
                            ? new AnthropicFoundryClient(new AnthropicFoundryIdentityTokenCredentials(AzureCredentialHelper.Create(), foundryResource))
                                .AsIChatClient(azureProvider.DeploymentName, AnthropicModelHelper.GetMaxOutputTokens(azureProvider.Model))
                            : new AnthropicFoundryClient(new AnthropicFoundryApiKeyCredentials(azureProvider.ApiKey, foundryResource))
                                .AsIChatClient(azureProvider.DeploymentName, AnthropicModelHelper.GetMaxOutputTokens(azureProvider.Model));

                        return new ChatClientBuilder(innerChatClient)
                            .UseKernelFunctionInvocation()
                            .Build();
                    }
                    builder.Services.AddSingleton(CreateChatClient);
                    builder.Services.AddSingleton(sp =>
                        sp.GetRequiredService<IChatClient>().AsChatCompletionService()
                    );
                }
                else if (azureProvider.UseManagedIdentity)
                {
                    builder.AddAzureOpenAIChatCompletion(modelId: azureProvider.Model,
                        credentials: AzureCredentialHelper.Create(),
                        endpoint: azureProvider.Endpoint,
                        deploymentName: azureProvider.DeploymentName,
                        apiVersion: azureProvider.ApiVersion
                    );
                }
                else
                {
                    builder.AddAzureOpenAIChatCompletion(modelId: azureProvider.Model,
                        apiKey: azureProvider.ApiKey,
                        endpoint: azureProvider.Endpoint,
                        deploymentName: azureProvider.DeploymentName,
                        apiVersion: azureProvider.ApiVersion
                    );
                }
            }
            else if (provider is OpenAIProvider openAIProvider)
            {
                builder = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion(modelId: openAIProvider.Model, apiKey: openAIProvider.ApiKey);
            }
            else if (provider is AnthropicAIProvider anthropicAIProvider)
            {
                IChatClient CreateChatClient(IServiceProvider sp)
                {
                    return new ChatClientBuilder(
                            new AnthropicClient(new ClientOptions { ApiKey = anthropicAIProvider.ApiKey })
                                .AsIChatClient(anthropicAIProvider.Model, anthropicAIProvider.MaxOutputTokens))
                        .UseKernelFunctionInvocation()
                        .Build();
                }
                builder = Kernel.CreateBuilder();
                builder.Services.AddSingleton(CreateChatClient);
                builder.Services.AddSingleton(sp =>
                    sp.GetRequiredService<IChatClient>().AsChatCompletionService()
                );
            }
            else if (provider is GoogleAIProvider googleAIProvider)
            {
                builder = Kernel.CreateBuilder();
                builder.AddGoogleAIGeminiChatCompletion(
                    modelId: googleAIProvider.Model,
                    apiKey: googleAIProvider.ApiKey
                );
            }
            else
            {
                return null;
            }

            builder.Services.AddScoped<IFunctionInvocationFilter, SemanticKernelInvocationLogger>();
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter, SemanticKernelAutoInvocationLogger>();
            builder.Services.AddScoped(sp => chatNotificationService);
            builder.Services.AddScoped(sp => chatContext);
            builder.Services.AddScoped<IDateTimeProvider>(sp => dateTimeProvider);
            if (mcpService != null) builder.Services.AddScoped(sp => mcpService);
            if (userContext != null) builder.Services.AddScoped(sp => userContext);
            if (tenantContext != null) builder.Services.AddScoped(sp => tenantContext);
            if (blobStorageProvider != null) builder.Services.AddScoped<IBlobStorageProvider>(sp => blobStorageProvider);
            if (eisKnowledgeService != null) builder.Services.AddScoped<IEisKnowledgeProvider>(sp => eisKnowledgeService);
            // Task 7961: lets SemanticKernelInvocationLogger report a failed tool to the chat as a warning.
            if (chatTurnErrorReporter != null) builder.Services.AddScoped(sp => chatTurnErrorReporter);
            // Bug: the kernel has its own service container, and a bare AddLogging() there has no providers, so
            // everything logged through it (SemanticKernelInvocationLogger's "Tool ... failed" error, the
            // auto-invocation logger) went nowhere - a failed tool showed only as WKFLOAI1113. The host's logger
            // factory is registered first so AddLogging (TryAdd) keeps it and those logs reach the app's sinks.
            // Registered as an instance, so disposing the kernel never disposes the host's factory.
            if (loggerFactory != null) builder.Services.AddSingleton(loggerFactory);
            builder.Services.AddLogging();

            return builder.Build();
        }

        /// <summary>
        /// Foundry's client only accepts a bare resource name (e.g. "example-resource"), while
        /// the Azure OpenAI branch's <c>Endpoint</c> convention is a full base URL. Accept either:
        /// if a full URL was stored, pull the first host label out of it.
        /// </summary>
        private static string ExtractFoundryResourceName(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return endpoint;
            }

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                var firstDotIndex = host.IndexOf('.');
                return firstDotIndex > 0 ? host[..firstDotIndex] : host;
            }

            return endpoint;
        }
    }
}
