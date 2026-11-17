using AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes;
using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class Secret : Entity<int>
    {
        public string Name { get; set; }

        // Stored unencrypted — never let its plain-text value reach AuditLogEntry.Changes.
        [SensitiveAuditField]
        public string ApiKey { get; set; }

        public bool IsActive { get; set; }

        public virtual ICollection<AIModel> AIModels { get; set; }
    }
}
