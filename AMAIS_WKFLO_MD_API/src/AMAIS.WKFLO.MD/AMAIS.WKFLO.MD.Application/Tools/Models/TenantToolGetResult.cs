using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class TenantToolGetResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public bool IsActive { get; set; }
        public bool IsPublished { get; set; }
        public bool IsShared { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
        public DateTimeOffset? LastPublishedOn { get; set; }
        public Guid? McpOAuthConnectionId { get; set; }
        public int? MaxToolOutputLength { get; set; }

        /// <summary>WI 7575: whether agent tool permissions apply to this tool.</summary>
        public bool PermissionsEnabled { get; set; }
        public IEnumerable<TagGetResult> Tags { get; set; } = [];

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantTool, TenantToolGetResult>()
        //                 .ForMember(dest => dest.IsPublished, opt => opt.MapFrom(src => src.PublishedTenantTool != null))
        //                 .ForMember(dest => dest.IsShared, opt => opt.MapFrom(src => src.PublishedTenantTool.MarketplaceTool != null))
        //                 .ForMember(dest => dest.LastPublishedOn, opt => opt.MapFrom(m => m.PublishedTenantTool.LastUpdatedOn != null ? m.PublishedTenantTool.LastUpdatedOn : m.PublishedTenantTool.CreatedOn));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantTool, TenantToolGetResult>()
                // US 7849: never return the stored OAuth client secret; expose hasClientSecret instead.
                .Map(dest => dest.Config, src => McpClientSecretHelper.SanitizeForResponse(src.Type, src.Config))
                .Map(dest => dest.IsPublished, src => src.PublishedTenantTool != null)
                .Map(dest => dest.IsShared, src => src.PublishedTenantTool != null && src.PublishedTenantTool.MarketplaceTool != null)
                .Map(dest => dest.LastPublishedOn, src => src.PublishedTenantTool != null
                    ? (src.PublishedTenantTool.LastUpdatedOn != null ? src.PublishedTenantTool.LastUpdatedOn : (DateTimeOffset?)src.PublishedTenantTool.CreatedOn)
                    : (DateTimeOffset?)null);
        }
    }
}
