using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// US 7974 / Task 7980: when the chat images of a tenant were last purged, as the tenant's local date (in its
    /// time zone), so the purge runs once per local day at the tenant's midnight. One row per tenant, created on its
    /// first purge. The row with a null <see cref="TenantId"/> tracks images that belong to no known tenant (legacy
    /// blob names), which are purged at UTC midnight.
    /// </summary>
    public class ChatImagePurgeState : Entity<int>
    {
        public int? TenantId { get; set; }
        public virtual Tenant Tenant { get; set; }

        public DateOnly LastPurgeLocalDate { get; set; }
    }
}
