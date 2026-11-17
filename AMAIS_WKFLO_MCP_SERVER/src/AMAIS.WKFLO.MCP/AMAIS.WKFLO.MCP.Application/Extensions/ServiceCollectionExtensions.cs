using AMAIS.WKFLO.Common.Infrastructure.Extensions;
using AMAIS.WKFLO.Common.Mappings;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AMAIS.WKFLO.MCP.Application.Extensions
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

            return services;
        }
    }
}

