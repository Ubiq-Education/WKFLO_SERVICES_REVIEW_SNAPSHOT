using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    public class TenantContextResolver
    {
        private readonly RequestDelegate _next;

        public TenantContextResolver(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
            var db = context.RequestServices.GetRequiredService<IMasterDataDbContext>();

            var tenantName = context.Request.Headers[CommonConsts.TenantName].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantName))
            {
                tenantContext.TenantName = tenantName;
            }

            // TenantGuid is the primary key to identify tenant, if it's provided, it will be used to identify tenant, otherwise, TenantName will be used.
            // WKFLO UI should use TenantGuid to identify tenant, and TenantName is only for backward compatibility, it will be deprecated in the future.
            var tenantGuid = context.Request.Headers[CommonConsts.TenantGuid].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantGuid))
            {
                tenantContext.TenantGuid = tenantGuid;
                var tenant = await db.Tenants.Where(m => m.Guid.ToString() == tenantGuid).FirstOrDefaultAsync();
                tenantContext.TenantName = tenant.Name;
            }
            else if (!string.IsNullOrWhiteSpace(tenantName))
            {
                // Backward-compatibility path: only TenantName was provided (no TenantGuid header). Resolve
                // the canonical TenantGuid from it, mirroring the GUID->Name resolution above, so consumers
                // that key off TenantGuid rather than TenantName still work for these callers — notably the
                // audit trail's actor context (AuditActorContextResolver) and its read-side query
                // (ListAuditLogQueryHandler), which both filter/stamp strictly by TenantGuid. Without this,
                // TenantGuid stays blank for every TenantName-only request and audit rows for it are silently
                // never written (MasterDataDbContext.AppendAuditLogEntries skips entirely when TenantGuid is
                // blank) nor ever visible when listed back (ListAuditLogQueryHandler filters by TenantGuid).
                // If the name doesn't resolve to a real tenant, leave TenantGuid unset — same as today's
                // behavior for an unresolved TenantName, so unrelated flows that already tolerate that aren't
                // newly broken here.
                var tenant = await db.Tenants.Where(m => m.Name == tenantName).FirstOrDefaultAsync();
                if (tenant != null)
                {
                    tenantContext.TenantGuid = tenant.Guid.ToString();
                }
            }

            var apiKey = context.Request.Headers[CommonConsts.ApiKey].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                tenantContext.ApiKey = apiKey;
            }

            var domain = context.Request.Headers[CommonConsts.Domain].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(domain))
            {
                tenantContext.Domain = domain;
            }

            await _next(context);
        }
    }
}
