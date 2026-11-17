using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class MarketplaceAgentUpdateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public int? AIModelId { get; set; }
        public string IconUrl { get; set; }
        public bool EnableSTM { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public IEnumerable<MarketplaceAgentToolResult> AgentTools { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<MarketplaceAgent, MarketplaceAgentUpdateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<MarketplaceAgent, MarketplaceAgentUpdateResult>();
        }
    }
}


