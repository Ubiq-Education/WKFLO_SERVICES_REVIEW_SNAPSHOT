using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    /// <summary>
    /// Copies the resolved <see cref="TenantContext"/>/<see cref="UserContext"/> onto the current request's
    /// <see cref="IMasterDataDbContext"/> so <c>MasterDataDbContext.SaveChangesAsync</c> can attribute audit
    /// log rows to the right tenant/actor.
    /// </summary>
    /// <remarks>
    /// Must run after <see cref="TenantContextResolver"/>, <see cref="UserContextResolver"/>, and
    /// <c>AutoUserCreationMiddleware</c>, so tenant and actor identity are fully resolved by the time this
    /// runs. Setting this via a middleware call (rather than constructor injection into
    /// <c>MasterDataDbContext</c>) is required because that context is registered with
    /// <c>AddDbContextPool</c>: a pooled instance's constructor only runs once per pool slot, not once per
    /// request, so a constructor-injected scoped <see cref="TenantContext"/>/<see cref="UserContext"/> would
    /// be captured from whichever request first created that pooled instance and silently go stale (wrong
    /// tenant/actor) on every later request that reuses the same pooled instance.
    /// </remarks>
    public class AuditActorContextResolver
    {
        private readonly RequestDelegate _next;

        public AuditActorContextResolver(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var db = context.RequestServices.GetRequiredService<IMasterDataDbContext>();
            var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
            var userContext = context.RequestServices.GetRequiredService<UserContext>();

            db.SetAuditActor(
                tenantContext.TenantGuid,
                userContext.Guid,
                userContext.UserName,
                userContext.Email,
                context.TraceIdentifier);

            await _next(context);
        }
    }
}
