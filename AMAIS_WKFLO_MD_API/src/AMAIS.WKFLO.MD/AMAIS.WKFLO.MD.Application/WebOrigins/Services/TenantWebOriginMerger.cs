using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Services
{
    public sealed record TenantWebOriginInput(string WebOriginType, string Name, bool IsActive);

    /// <summary>
    /// Add-only merge of incoming web origins into a tenant's stored origins.
    /// Never deletes or deactivates rows: the same tenant may be enabled from several environments
    /// (e.g. staging and production), each contributing its own origins.
    /// </summary>
    public static class TenantWebOriginMerger
    {
        public static string NormalizeName(string name) => (name ?? string.Empty).Trim().TrimEnd('/');

        /// <summary>
        /// Merges <paramref name="incoming"/> into the tenant's origins and saves.
        /// Returns the tenant's full list of non-deleted origins.
        /// </summary>
        public static async Task<List<WebOrigin>> MergeAsync(
            IMasterDataDbContext db,
            Tenant tenant,
            IEnumerable<TenantWebOriginInput> incoming,
            CancellationToken cancellationToken)
        {
            var existing = await db.WebOrigins
                .Where(m => m.TenantId == tenant.Id)
                .ToListAsync(cancellationToken);

            var added = new List<WebOrigin>();

            // Collapse duplicates (same type + normalized name) up front; the origin is active
            // if any duplicate says so, so the result doesn't depend on the order of the items.
            var deduped = incoming
                .Select(i => new TenantWebOriginInput(i.WebOriginType, NormalizeName(i.Name), i.IsActive))
                .Where(i => i.Name.Length > 0)
                .GroupBy(i => ((i.WebOriginType ?? string.Empty).ToLowerInvariant(), i.Name.ToLowerInvariant()))
                .Select(g => g.First() with { IsActive = g.Any(i => i.IsActive) });

            foreach (var item in deduped)
            {
                var name = item.Name;
                var type = item.WebOriginType ?? string.Empty;

                var matches = existing.Concat(added)
                    .Where(o => string.Equals(o.WebOriginType ?? string.Empty, type, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(NormalizeName(o.Name), name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 0)
                {
                    added.Add(new WebOrigin
                    {
                        WebOriginType = item.WebOriginType,
                        Name = name,
                        IsActive = item.IsActive,
                        TenantId = tenant.Id,
                        Tenant = tenant
                    });
                    continue;
                }

                // Repair legacy rows stored with a trailing slash / whitespace so CORS can match them.
                foreach (var match in matches.Where(o => o.Name != NormalizeName(o.Name)))
                    match.Name = NormalizeName(match.Name);

                if (matches.Any(o => o.IsActive && !o.IsDeleted)) continue;

                // Only bring a deleted/inactive origin back when the caller asks for it to be active.
                if (!item.IsActive) continue;

                var toReactivate = matches[0];
                toReactivate.IsDeleted = false;
                toReactivate.IsActive = true;
            }

            if (added.Count > 0)
                await db.WebOrigins.AddRangeAsync(added, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return existing.Concat(added).Where(o => !o.IsDeleted).ToList();
        }
    }
}
