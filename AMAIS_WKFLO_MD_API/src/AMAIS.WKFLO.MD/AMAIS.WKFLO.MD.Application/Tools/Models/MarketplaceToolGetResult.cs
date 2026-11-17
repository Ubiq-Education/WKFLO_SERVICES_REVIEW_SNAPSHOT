using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class MarketplaceToolGetResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public int Stars { get; set; }
        public int Installations { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<MarketplaceTool, MarketplaceToolGetResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<MarketplaceTool, MarketplaceToolGetResult>()
                // US 7849: never return the stored OAuth client secret; expose hasClientSecret instead.
                .Map(dest => dest.Config, src => McpClientSecretHelper.SanitizeForResponse(src.Type, src.Config));
        }
    }
}


