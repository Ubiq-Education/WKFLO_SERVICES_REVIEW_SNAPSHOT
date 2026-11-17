using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class MarketplaceAgentGetResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>The FK this agent's AI Model is actually stored under (<c>Agent.AIModelId</c>) —
        /// distinct from <see cref="AIModelName"/>/<see cref="Model"/>/<see cref="Provider"/> below,
        /// which describe the resolved model but can't be used to pre-select an edit form's AI Model
        /// dropdown by id.</summary>
        public int? AIModelId { get; set; }
        public string AIModelName { get; set; }
        public string Model { get; set; }
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public string Endpoint { get; set; }
        public string SystemPrompt { get; set; }
        public string Type { get; set; }
        public int Stars { get; set; }
        public int Installations { get; set; }
        /// <summary>Absolute URL of the agent's icon (copied from the source TenantAgent's chat icon
        /// at share-time — see <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.MarketplaceAgent.IconUrl"/>).
        /// Null for agents shared before this field existed, or whose source agent never had a chat
        /// icon configured.</summary>
        public string IconUrl { get; set; }
        /// <summary>Whether a <c>TenantAgent</c> installed from this template gets Short Term Memory
        /// enabled by default (see <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.MarketplaceAgent.EnableSTM"/>'s
        /// doc comment) - lets the edit dialog pre-select the current value.</summary>
        public bool EnableSTM { get; set; }
        public IEnumerable<MarketplaceAgentToolResult> AgentTools { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        /// <summary>Tags currently assigned to this Marketplace agent (via <c>EntityTag</c>,
        /// <c>EntityType == "Agent"</c>), resolved cross-tenant by <see cref="Queries.ListMarketplaceAgents.ListMarketplaceAgentsQueryHandler"/>
        /// - same rationale as the <c>Tag</c> filter on <see cref="Queries.ListMarketplaceAgents.ListMarketplaceAgentsQuery"/>:
        /// Marketplace agents are a global catalog, so a tenant-scoped lookup (like
        /// <c>ListTagsByEntityQuery</c>) would incorrectly return an empty list here whenever the
        /// tag was assigned under a tenant other than the caller's currently selected one.</summary>
        public IEnumerable<TagGetResult> Tags { get; set; } = new List<TagGetResult>();

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<MarketplaceAgent, MarketplaceAgentGetResult>()
        //                 .ForMember(dest => dest.AIModelName, source => source.MapFrom(m => m.AIModel.Name))
        //                 .ForMember(dest => dest.Provider, source => source.MapFrom(m => m.AIModel.Provider))
        //                 .ForMember(dest => dest.Model, source => source.MapFrom(m => m.AIModel.Model))
        //                 .ForMember(dest => dest.Type, source => source.MapFrom(m => m.AIModel.Type))
        //                 .ForMember(dest => dest.ApiKey, source => source.MapFrom(m => m.AIModel.Secret.ApiKey));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<MarketplaceAgent, MarketplaceAgentGetResult>()
                .Map(dest => dest.AIModelName, src => src.AIModel != null ? src.AIModel.Name : null)
                .Map(dest => dest.Provider, src => src.AIModel != null ? src.AIModel.Provider : null)
                .Map(dest => dest.Model, src => src.AIModel != null ? src.AIModel.Model : null)
                .Map(dest => dest.Type, src => src.AIModel != null ? src.AIModel.Type : null)
                .Map(dest => dest.ApiKey, src => src.AIModel != null && src.AIModel.Secret != null ? src.AIModel.Secret.ApiKey : null);
        }
    }
}
