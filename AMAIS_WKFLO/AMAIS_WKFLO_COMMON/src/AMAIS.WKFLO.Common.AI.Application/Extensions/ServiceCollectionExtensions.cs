using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAIApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<ITenantAISyncService, TenantAISyncService>();
            services.AddScoped<IPublicAISyncService, PublicAISyncService>();
            services.AddScoped<IRunnableAISyncService, RunnableAISyncService>();
            services.AddScoped<ISemanticKernelAgentService, SemanticKernelAgentService>();
            services.AddScoped<IConversationAnalysisWorkflowScheduler, DaprConversationAnalysisWorkflowScheduler>();
            services.AddScoped<IConversationTitleWorkflowScheduler, DaprConversationTitleWorkflowScheduler>();
            services.AddScoped<IMessageResolutionWorkflowScheduler, DaprMessageResolutionWorkflowScheduler>();

            // Bug 8189 / Task 8219: large chat attachments (indexing and per-turn retrieval)
            services.AddScoped<IChatDocumentIndexer, ChatDocumentIndexer>();
            services.AddScoped<IChatDocumentRetriever, ChatDocumentRetriever>();

            // US 7501: Stop. The registry is per replica (singleton); the host may replace the null broadcaster
            // with a real one (Dapr pub/sub in the AI API).
            services.AddSingleton<IChatTurnRegistry, ChatTurnRegistry>();
            // US 7506: the session working state the registry keeps (Dapr state, shared by every replica).
            services.AddSingleton<ISessionWorkingStateStore, SessionWorkingStateStore>();
            services.AddSingleton<ChatStopBroadcastHandler>();
            services.AddScoped<IChatStopStateStore, ChatStopStateStore>();
            services.AddScoped<IChatSessionOwnerStore, ChatSessionOwnerStore>();
            services.AddScoped<IChatStopCheckpoint, ChatStopCheckpoint>();
            services.TryAddSingleton<IChatStopBroadcaster, NullChatStopBroadcaster>();

            // US 7959: chat runtime errors. One failure state per turn scope; the notifier reports it to the chat once.
            services.AddScoped<IChatTurnFailureState, ChatTurnFailureState>();
            services.AddScoped<IChatTurnErrorNotifier, ChatTurnErrorNotifier>();

            // Task 7961: tool warnings of the running turn, reported live (once per tool per turn).
            services.AddScoped<IChatTurnErrorReporter, ChatTurnErrorReporter>();

            return services;
        }
    }
}
