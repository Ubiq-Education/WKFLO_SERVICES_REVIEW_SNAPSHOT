using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;

namespace AMAIS.WKFLO.Common.Infrastructure.Extensions
{
    public static class OpenTelemetryExtensions
    {
        public static ResourceBuilder CreateWkfloResourceBuilder(string serviceName, IHostEnvironment environment)
        {
            return ResourceBuilder.CreateDefault()
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName
                });
        }
    }
}
