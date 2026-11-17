using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;

namespace AMAIS.WKFLO.Common.Infrastructure.Extensions
{
    public static class AutoUserCreationMiddlewareExtensions
    {
        public static IApplicationBuilder UseAutoUserCreation(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AutoUserCreationMiddleware>();
        }
    }
}