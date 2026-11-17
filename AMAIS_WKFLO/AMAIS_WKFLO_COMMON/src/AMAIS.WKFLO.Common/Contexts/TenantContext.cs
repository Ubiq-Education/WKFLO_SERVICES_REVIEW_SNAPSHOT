using AMAIS.WKFLO.Common.Mappings;
using Mapster;

namespace AMAIS.WKFLO.Common.Contexts
{
    public class TenantContext : IMap
    {
        public string TenantName { get; set; }
        public string TenantGuid { get; set; }
        public string ApiKey { get; set; }
        public string Domain { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantContext, TenantContext>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantContext, TenantContext>();
        }
    }
}

