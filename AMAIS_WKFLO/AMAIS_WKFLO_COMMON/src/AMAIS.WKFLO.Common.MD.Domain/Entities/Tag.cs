using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class Tag : Entity<int>
    {
        public string Name { get; set; }
        public string Color { get; set; }
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }
        public virtual ICollection<EntityTag> EntityTags { get; set; }
    }
}
