using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Models
{
    public class TenantWebOriginResult : IMap
    {
        public int Id { get; set; }
        public string WebOriginType { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<WebOrigin, TenantWebOriginResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<WebOrigin, TenantWebOriginResult>();
        }
    }
}
