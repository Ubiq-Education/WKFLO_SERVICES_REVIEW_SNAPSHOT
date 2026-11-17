using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.Amais;
using AMAIS.WKFLO.Common.AI.Application.Services.Mcp;
using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Repositories;
using AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AMAIS.WKFLO.AI.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
        {
            // IWkfloMapper — swap the line below to switch mapping backend without touching handlers.
            //services.AddAutoMapperWkfloMapper();
            services.AddMapsterWkfloMapper();
            services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
            services.AddMediatorBehaviors();
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            // Registers the token store too (see AddMcpOAuthConsentService's remarks) — needed for the
            // public chat widget's own MCP OAuth "start" entry point (EndPoints/Public/Tools).
            services.AddMcpOAuthConsentService();
            services.AddScoped<IMcpClientService, McpClientService>();
            services.AddScoped<IChatStoreService, ChatStoreService>();
            services.AddScoped<IAmaisTokenApiService, AmaisTokenApiService>();
            services.AddScoped<IContactFormReplayStateService, ContactFormReplayStateService>();
            services.AddScoped<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.IToolApprovalStateService, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalStateService>();
            services.AddScoped<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.IToolApprovalExpiryScheduler, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.DaprToolApprovalExpiryScheduler>();
            services.AddScoped<AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.IToolApprovalNotifier, AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval.ToolApprovalNotifier>();
            services.AddScoped<Agents.Commands.ResumeToolApproval.IToolApprovalResumeQueue, Agents.Commands.ResumeToolApproval.BackgroundToolApprovalResumeQueue>();
            services.AddScoped<Agents.Commands.DecideToolApproval.ToolApprovalDecisionProcessor>();
            services.AddScoped<Agents.Commands.DecideToolApproval.ToolApprovalSessionGate>();
            services.AddScoped<Agents.Commands.StopChatSession.StopChatSessionProcessor>();
            services.AddScoped<Agents.Commands.StopChatSession.StoppableSyncRun>();
            services.AddScoped<Agents.Commands.StopChatSession.ISessionHandOffQueueClearer, Agents.Commands.StopChatSession.SessionHandOffQueueClearer>();
            services.AddScoped<Agents.Commands.StopChatSession.IHandOffQueueClearScheduler, Agents.Commands.StopChatSession.BackgroundHandOffQueueClearScheduler>();
            services.AddScoped<Agents.Actors.IHandOffQueueStopGuard, Agents.Actors.HandOffQueueStopGuard>();
            services.AddSingleton<Agents.Commands.StopChatSession.IDroppedHandOffMarkerSaver, Agents.Commands.StopChatSession.DroppedHandOffMarkerSaver>();
            services.AddScoped<Agents.ChatErrors.IChatAgentFailureReporter, Agents.ChatErrors.ChatAgentFailureReporter>();

            services.AddScoped<IUserRepository, UserRepository>();

            services.AddAIApplication(configuration);

            return services;
        }
    }
}

