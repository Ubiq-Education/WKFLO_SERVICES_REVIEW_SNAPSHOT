using AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes;
using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class DxpIntegration : Entity<int>
    {
        public int TenantId { get; set; }
        public virtual Tenant Tenant { get; set; }

        public string DxpTenantName { get; set; }
        public string Domain { get; set; }

        // Stored unencrypted — never let its plain-text value reach AuditLogEntry.Changes.
        [SensitiveAuditField]
        public string ApiKey { get; set; }

        public bool IsActive { get; set; }
    }
}
