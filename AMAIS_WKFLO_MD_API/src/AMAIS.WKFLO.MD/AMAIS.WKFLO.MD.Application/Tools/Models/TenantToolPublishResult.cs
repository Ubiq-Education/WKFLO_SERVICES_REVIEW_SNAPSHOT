using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class TenantToolPublishResult : IMap
    {
        public int Id { get; set; }
        public string ToolType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedByTenantToolId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<PublishedTenantTool, TenantToolPublishResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<PublishedTenantTool, TenantToolPublishResult>()
                // US 7849: never return the stored OAuth client secret; expose hasClientSecret instead.
                .Map(dest => dest.Config, src => McpClientSecretHelper.SanitizeForResponse(src.Type, src.Config));
        }
    }
}

