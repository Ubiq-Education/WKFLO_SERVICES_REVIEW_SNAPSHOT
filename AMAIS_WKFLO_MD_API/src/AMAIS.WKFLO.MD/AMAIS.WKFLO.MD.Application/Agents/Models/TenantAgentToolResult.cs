using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentToolResult : IMap
    {
        public int ToolId { get; set; }
        public string ToolName { get; set; }
        public string ToolDescription { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AgentTool, TenantAgentToolResult>()
        //                 .ForMember(dest => dest.ToolId, o => o.MapFrom(source => source.ToolId))
        //                 .ForMember(dest => dest.ToolName, o => o.MapFrom(source => source.Tool.Name))
        //                 .ForMember(dest => dest.ToolDescription, o => o.MapFrom(source => source.Tool.Description));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AgentTool, TenantAgentToolResult>()
                .Map(dest => dest.ToolId, src => src.ToolId)
                .Map(dest => dest.ToolName, src => src.Tool != null ? src.Tool.Name : null)
                .Map(dest => dest.ToolDescription, src => src.Tool != null ? src.Tool.Description : null);
        }
    }
}
