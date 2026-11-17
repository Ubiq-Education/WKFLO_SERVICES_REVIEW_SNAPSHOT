using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class WebOrigin : Entity<int>
    {
        public string WebOriginType { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }
    }
}
