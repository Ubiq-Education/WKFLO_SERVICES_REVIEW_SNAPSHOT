using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class Tenant : Entity<int>
    {
        public string Name { get; set; }
        public Guid Guid { get; set; }
        public string Region { get; set; }
    }
}
