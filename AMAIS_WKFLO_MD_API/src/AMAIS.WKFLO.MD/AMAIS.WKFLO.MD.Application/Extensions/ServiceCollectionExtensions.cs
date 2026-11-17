using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Infrastructure.Repositories;
using AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AMAIS.WKFLO.MD.Application.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // IWkfloMapper — swap the line below to switch mapping backend without touching handlers.
            //services.AddAutoMapperWkfloMapper();
            services.AddMapsterWkfloMapper();
            services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
            services.AddMediatorBehaviors();
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

            services.AddScoped<IChatStoreService, ChatStoreService>();

            services.AddScoped<IUserRepository, UserRepository>();

            // Persisted (SQL-backed) pending-flow state — see McpOAuthConsentService's remarks — so this can
            // be registered as an ordinary scoped-lifetime service, not a process-wide singleton.
            services.AddMcpOAuthConsentService();

            return services;
        }
    }
}

