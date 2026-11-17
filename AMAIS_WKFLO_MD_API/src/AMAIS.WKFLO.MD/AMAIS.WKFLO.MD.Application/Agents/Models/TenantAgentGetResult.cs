using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class TenantAgentGetResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string AIModelName { get; set; }
        public int? AIModelId { get; set; }
        public string Model { get; set; }
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public string Endpoint { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseTone { get; set; }
        /// <summary>Admin-supplied instructions appended after the SystemPrompt at runtime (WI 7318). Null when not set.</summary>
        public string AdditionalInstructions { get; set; }
        public string Type { get; set; }
        /// <summary>Absolute URL of the agent's icon (see <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.Agent.IconUrl"/>
        /// for how/when it gets populated - e.g. copied from a MarketplaceAgent at install time).
        /// Null when no icon has ever been set for this agent.</summary>
        public string IconUrl { get; set; }
        /// <summary>Id of the <see cref="MarketplaceAgent"/> this agent was installed from, if any
        /// (see <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.TenantAgent.InstalledFromMarketplaceAgentId"/>).
        /// Null for agents never installed from the catalog (e.g. MAISY, manually created agents).</summary>
        public int? InstalledFromMarketplaceAgentId { get; set; }
        public bool IsActive { get; set; }
        public bool EnableSTM { get; set; }

        public bool EnableLTM { get; set; }
        public bool EnableSpinUpAgents { get; set; }
        public int? MaxToolCallsPerTurn { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public IEnumerable<TenantAgentToolResult> AgentTools { get; set; }
        public bool IsPublished { get; set; }
        public bool IsShared { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
        public DateTimeOffset? LastPublishedOn { get; set; }
        public IEnumerable<TagGetResult> Tags { get; set; } = [];

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantAgent, TenantAgentGetResult>()
        //                 .ForMember(dest => dest.AIModelName, opt => opt.MapFrom(m => m.AIModel.Name))
        //                 .ForMember(dest => dest.AIModelId, opt => opt.MapFrom(m => m.AIModel.Id))
        //                 .ForMember(dest => dest.Provider, opt => opt.MapFrom(m => m.AIModel.Provider))
        //                 .ForMember(dest => dest.Model, opt => opt.MapFrom(m => m.AIModel.Model))
        //                 .ForMember(dest => dest.Type, opt => opt.MapFrom(m => m.AIModel.Type))
        //                 .ForMember(dest => dest.ApiKey, opt => opt.MapFrom(m => m.AIModel.Secret.ApiKey))
        //                 .ForMember(dest => dest.IsPublished, opt => opt.MapFrom(m => m.PublishedTenantAgent != null))
        //                 .ForMember(dest => dest.IsShared, opt => opt.MapFrom(m => m.PublishedTenantAgent.MarketplaceAgent != null))
        //                 .ForMember(dest => dest.LastPublishedOn, opt => opt.MapFrom(m => m.PublishedTenantAgent.LastUpdatedOn != null ? m.PublishedTenantAgent.LastUpdatedOn : m.PublishedTenantAgent.CreatedOn));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantAgent, TenantAgentGetResult>()
                .Map(dest => dest.AIModelName, src => src.AIModel != null ? src.AIModel.Name : null)
                .Map(dest => dest.AIModelId, src => src.AIModel != null ? (int?)src.AIModel.Id : null)
                .Map(dest => dest.Provider, src => src.AIModel != null ? src.AIModel.Provider : null)
                .Map(dest => dest.Model, src => src.AIModel != null ? src.AIModel.Model : null)
                .Map(dest => dest.Type, src => src.AIModel != null ? src.AIModel.Type : null)
                .Map(dest => dest.ApiKey, src => src.AIModel != null && src.AIModel.Secret != null ? src.AIModel.Secret.ApiKey : null)
                .Map(dest => dest.IsPublished, src => src.PublishedTenantAgent != null)
                .Map(dest => dest.IsShared, src => src.PublishedTenantAgent != null && src.PublishedTenantAgent.MarketplaceAgent != null)
                .Map(dest => dest.LastPublishedOn, src => src.PublishedTenantAgent != null
                    ? (src.PublishedTenantAgent.LastUpdatedOn != null ? src.PublishedTenantAgent.LastUpdatedOn : (DateTimeOffset?)src.PublishedTenantAgent.CreatedOn)
                    : (DateTimeOffset?)null);
        }
    }
}
