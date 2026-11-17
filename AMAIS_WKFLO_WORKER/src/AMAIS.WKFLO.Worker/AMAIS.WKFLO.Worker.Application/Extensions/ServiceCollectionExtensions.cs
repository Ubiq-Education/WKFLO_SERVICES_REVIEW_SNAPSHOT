using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore;
using AMAIS.WKFLO.Common.Infrastructure.Services.DXP;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Worker.Application.Jobs;
using AMAIS.WKFLO.Worker.Application.Jobs.TopicClassification;
using AMAIS.WKFLO.Worker.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMAIS.WKFLO.Worker.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkerApplication(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<WorkerSettings>(configuration.GetSection(nameof(WorkerSettings)));

            services.AddBlobStorageConfiguration(configuration);
            services.AddScoped<IChatStoreService, ChatStoreService>();

            // DXP chatbot analytics reporting (batch topic classification path — see event 4 in US 5911).
            // AddHttpContextAccessor is required even though this job never runs inside a real HTTP request —
            // DxpChatbotAnalyticsService takes an IHttpContextAccessor dependency (falls back to placeholder
            // IP/UserAgent/Language values when HttpContext is null, which it always will be here).
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddAmaisDxpServices(configuration);
            services.AddScoped<IDxpChatbotAnalyticsService, DxpChatbotAnalyticsService>();

            services.AddScoped<IJobHandler, TopicClassificationJobHandler>();

            return services;
        }
    }
}
