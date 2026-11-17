using Microsoft.Extensions.DependencyInjection;

namespace AMAIS.WKFLO.Common.Mediator
{
    public static class WkfloMediatorExtensions
    {
        public static IServiceCollection AddMediatorWkfloMediator(this IServiceCollection services)
        {
            services.AddScoped<IWkfloMediator, MediatorWkfloMediator>();
            return services;
        }
    }
}
