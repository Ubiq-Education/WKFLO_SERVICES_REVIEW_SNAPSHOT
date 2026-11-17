using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.Tenants.Models
{
    public class TenantGetResult : IMapFrom<Tenant>
    {
        public int Id { get; set; }
        public string Guid { get; set; }
        public string Name { get; set; }
    }
}
