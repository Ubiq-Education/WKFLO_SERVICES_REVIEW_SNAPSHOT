using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class TenantToolShareResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public DateTimeOffset SharedAt { get; set; }
        public int? SharedByTenantId { get; set; }
        public string SharedByTenantName { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<MarketplaceTool, TenantToolShareResult>()
        //                 .ForMember(dest => dest.SharedAt, opt => opt.MapFrom(src => src.CreatedOn))
        //                 .ForMember(dest => dest.SharedByTenantId, opt => opt.MapFrom(src => src.TenantId))
        //                 .ForMember(dest => dest.SharedByTenantName, opt => opt.MapFrom(src => src.Tenant != null ? src.Tenant.Name : null));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<MarketplaceTool, TenantToolShareResult>()
                // US 7849: never return the stored OAuth client secret; expose hasClientSecret instead.
                .Map(dest => dest.Config, src => McpClientSecretHelper.SanitizeForResponse(src.Type, src.Config))
                .Map(dest => dest.SharedAt, src => src.CreatedOn)
                .Map(dest => dest.SharedByTenantId, src => src.TenantId)
                .Map(dest => dest.SharedByTenantName, src => src.Tenant != null ? src.Tenant.Name : null);
        }
    }
}
