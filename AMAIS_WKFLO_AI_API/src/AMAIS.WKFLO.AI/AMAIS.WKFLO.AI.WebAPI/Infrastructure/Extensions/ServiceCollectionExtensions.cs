using AMAIS.PUM.Log;
using AMAIS.WKFLO.AI.Application.Agents.Actors;
using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Invocation;
using AMAIS.WKFLO.AI.Application.Agents.Workflows;
using AMAIS.WKFLO.AI.Application.Agents.Workflows.MultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Common.Activities;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.HostedServices;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs.Services;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Providers;
using AMAIS.WKFLO.Common.Infrastructure.Services;
using AMAIS.WKFLO.Common.Infrastructure.Services.DXP;
using AMAIS.WKFLO.Common.Infrastructure.Services.PUM;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Settings;
using Azure.Identity;
using Dapr.Actors.Client;
using Dapr.Client;
using Dapr.Workflow;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.Retry;
using IDateTimeProvider = AMAIS.WKFLO.Common.Interfaces.Providers.IDateTimeProvider;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the dynamic CORS policy provider and the singleton store of allowed origins it relies on.
        /// </summary>
        public static IServiceCollection AddDynamicCors(this IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<CorsOriginStore>();
            services.AddSingleton<ICorsPolicyProvider, DynamicCorsPolicyProvider>();
            services.AddCors();
            return services;
        }

        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions();
            services.Configure<ElasticsearchSettings>(configuration.GetSection(nameof(ElasticsearchSettings)));
            services.Configure<EisElasticsearchSettings>(configuration.GetSection(nameof(EisElasticsearchSettings)));
            services.Configure<AmaisTokenVoiceApiServiceSettings>(configuration.GetSection(nameof(AmaisTokenVoiceApiServiceSettings)));
            services.Configure<AmaisChatBotSettings>(configuration.GetSection(nameof(AmaisChatBotSettings)));

            services.AddScoped<TenantContext>();
            services.AddScoped<UserContext>();
            services.AddScoped<AgentSharableContext>();
            services.AddScoped<ChatContext>();
            services.AddScoped<IDateTimeProvider, DateTimeProvider>();
            services.AddDatastreamProvider(configuration);
            services.AddAmaisDatastreamProvider(configuration);
            services.AddBlobStorageConfiguration(configuration);
            services.AddAzureSpeechProvider(configuration);
            services.AddNpgsqlConfiguration(configuration);
            services.AddVectorStoreConfiguration(configuration);

            // Register notification service
            services.AddScoped<IChatNotificationService, ChatNotificationService>();

            // Runs one agent role/step of a multi-agent orchestration or a cross-agent pub/sub event.
            // Replaces the former AgentActor Dapr Actor (#6553) — Scoped, not an actor, since callers
            // (InvokeAgentWorkflowActivity, AgentEventWorkflowActivity) already provide a fresh DI scope.
            services.AddScoped<IAgentInvoker, AgentInvoker>();

            // DXP services
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddAmaisDxpServices(configuration);
            services.AddScoped<IDxpContactFormService, DxpContactFormService>();
            services.AddScoped<IDxpChatbotAnalyticsService, DxpChatbotAnalyticsService>();
            services.AddContactFormServices();

            // Register HttpClient and Dapr Client
            services.AddHttpClient();

            // Grpc.Net.Client defaults MaxReceiveMessageSize to 4MB regardless of daprd's own
            // --max-body-size (which only bounds what the sidecar itself accepts/sends). Queued
            // messages with image attachments can exceed that on GetStateAsync (BaseWorkflowActivity
            // reading the queued command back out of statestore-ai), so both directions need raising
            // to stay above CommonAiPubSubConsts.MaxQueueMessageSizeBytes.
            //
            // UseJsonSerializationOptions: queued messages carry base64 attachments; the default JSON
            // encoder escapes '+' and makes serialization allocate ~22x the payload (OOM in a 0.5 GiB
            // container, Bug 8189). Relaxed escaping is the same options object QueueMessagePayloadGuard uses.
            services.AddDaprClient(daprBuilder => daprBuilder
                .UseGrpcChannelOptions(new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = CommonConsts.CommonAiPubSubConsts.MaxGrpcMessageSizeBytes,
                    MaxSendMessageSize = CommonConsts.CommonAiPubSubConsts.MaxGrpcMessageSizeBytes
                })
                .UseJsonSerializationOptions(QueueMessageJsonOptions.Default));

            services.AddSingleton<IActorProxyFactory>(_ => new ActorProxyFactory());
            services.AddActors(options =>
            {
                // Reserved scaffolding for future use — intentionally unwired today (#6553).
                options.Actors.RegisterActor<WorkflowActor>();

                // Gatekeeper serializing AgentEventWorkflow execution per session/agent (#6589).
                options.Actors.RegisterActor<AgentActor>();
            });

            // 🔥 This enables workflows
            services.AddDaprWorkflow(options =>
            {
                //options.MaxConcurrentWorkflows = 2;
                //options.MaxConcurrentActivities = 2;

                // Register workflows
                options.RegisterWorkflow<MessageTenantAgentByIdWorkflow>();
                options.RegisterWorkflow<MessagePublicAgentByShareIdWorkflow>();
                options.RegisterWorkflow<VoiceMessageTenantAgentByIdWorkflow>();
                options.RegisterWorkflow<VoiceMessagePublicAgentByShareIdWorkflow>();
                options.RegisterWorkflow<ConversationAnalysisWorkflow>();
                options.RegisterWorkflow<ConversationTitleWorkflow>();
                options.RegisterWorkflow<MessageResolutionWorkflow>();
                options.RegisterWorkflow<PurgeChatImagesWorkflow>();
                options.RegisterWorkflow<MultiAgentOrchestratorWorkflow>();
                options.RegisterWorkflow<AgentEventWorkflow>();
                options.RegisterWorkflow<AMAIS.WKFLO.AI.Application.Agents.Workflows.ToolApprovalExpiryWorkflow>();

                // Register activities
                options.RegisterActivity<MessageTenantAgentByIdWorkflowActivity>();
                options.RegisterActivity<MessagePublicAgentByShareIdWorkflowActivity>();
                options.RegisterActivity<VoiceMessageTenantAgentByIdWorkflowActivity>();
                options.RegisterActivity<VoiceMessagePublicAgentByShareIdWorkflowActivity>();
                options.RegisterActivity<ConversationAnalysisWorkflowActivity>();
                options.RegisterActivity<ConversationTitleWorkflowActivity>();
                options.RegisterActivity<MessageResolutionWorkflowActivity>();
                options.RegisterActivity<PurgeChatImagesWorkflowActivity>();
                options.RegisterActivity<AgentEventWorkflowActivity>();
                options.RegisterActivity<InvokeAgentWorkflowActivity>();
                options.RegisterActivity<NotifyChatErrorWorkflowActivity>();
                options.RegisterActivity<AMAIS.WKFLO.AI.Application.Agents.Workflows.ToolApprovalExpiryWorkflowActivity>();
            });

            services.AddHostedService<PurgeChatImagesStartupService>();

            services.AddAmaisPumServices(configuration);

            return services;
        }

        public static IServiceCollection AddContactFormServices(this IServiceCollection services)
        {
            services.AddScoped<IContactFormPluginLogService, ContactFormPluginLogService>();
            return services;
        }

        public static IServiceCollection AddAmaisPumServices(this IServiceCollection services, IConfiguration configuration)
        {
            var usageLogClientSettingsSection = configuration.GetSection("UsageLogClientSettings");
            services.Configure<UsageLogClientSettings>(usageLogClientSettingsSection);

            var usageLogClientSettings = usageLogClientSettingsSection.Get<UsageLogClientSettings>();

            if (usageLogClientSettings != null)
            {
                DefaultAzureCredential credential = AzureCredentialHelper.Create();
                services.AddScoped(s => new UsageLogClient(usageLogClientSettings, credential));
                services.AddScoped<IPumUsageReporter, PumUsageReporter>();
            }
            else
            {
                services.AddScoped<IPumUsageReporter, NullPumUsageReporter>();
            }

            return services;
        }

        public static async Task WaitForDaprSidecarWithPollyAsync(CancellationToken cancellationToken = default)
        {
            using var client = new DaprClientBuilder().Build();

            // Define retry policy: exponential backoff, up to 10 retries
            AsyncRetryPolicy retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 10,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(2000),
                    onRetry: (outcome, timespan, attempt, context) =>
                    {
                        Console.WriteLine($"Waiting for Dapr sidecar... attempt {attempt}");
                    });

            // Execute wait-for-sidecar within Polly
            await retryPolicy.ExecuteAsync(async ct =>
            {
                await client.WaitForSidecarAsync(cancellationToken: cancellationToken);
                Console.WriteLine("Dapr sidecar is ready!");
            }, cancellationToken);
        }

    }
}
