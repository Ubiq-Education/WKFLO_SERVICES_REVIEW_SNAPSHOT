using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using Mapster;
using Newtonsoft.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.MD.Domain.Models.Tools
{
    public class ToolResult : IMap
    {
        public string ToolType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public AmaisIntegrationToolConfig AmaisIntegrationToolConfiguration { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantTool, ToolResult>()
        //                 .ForMember(dest => dest.AmaisIntegrationToolConfiguration, opt => opt.MapFrom(src =>
        //                     string.IsNullOrEmpty(src.Config) || src.Type != CommonToolTypeConsts.IntegrationAmais
        //                         ? null
        //                         : JsonConvert.DeserializeObject<AmaisIntegartionToolConfigurationResult>(src.Config)));
        // 
        //             profile.CreateMap<PublishedTenantTool, ToolResult>()
        //                 .ForMember(dest => dest.AmaisIntegrationToolConfiguration, opt => opt.MapFrom(src =>
        //                     string.IsNullOrEmpty(src.Config) || src.Type != CommonToolTypeConsts.IntegrationAmais
        //                         ? null
        //                         : JsonConvert.DeserializeObject<AmaisIntegartionToolConfigurationResult>(src.Config)));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantTool, ToolResult>()
                .Map(dest => dest.AmaisIntegrationToolConfiguration, src =>
                    string.IsNullOrEmpty(src.Config) || src.Type != CommonToolTypeConsts.IntegrationAmais
                        ? null
                        : JsonConvert.DeserializeObject<AmaisIntegrationToolConfig>(src.Config));

            config.NewConfig<PublishedTenantTool, ToolResult>()
                .Map(dest => dest.AmaisIntegrationToolConfiguration, src =>
                    string.IsNullOrEmpty(src.Config) || src.Type != CommonToolTypeConsts.IntegrationAmais
                        ? null
                        : JsonConvert.DeserializeObject<AmaisIntegrationToolConfig>(src.Config));
        }
    }

}
