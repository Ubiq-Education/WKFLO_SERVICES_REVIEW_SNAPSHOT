using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tenants.Models
{
    public class TenantCreateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public bool IsDeleted { get; set; }
        public Guid Guid { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
        public int Code { get; set; }
        public List<TenantWebOriginResult> WebOrigins { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<Tenant, TenantCreateResult>();
        }
    }
}


