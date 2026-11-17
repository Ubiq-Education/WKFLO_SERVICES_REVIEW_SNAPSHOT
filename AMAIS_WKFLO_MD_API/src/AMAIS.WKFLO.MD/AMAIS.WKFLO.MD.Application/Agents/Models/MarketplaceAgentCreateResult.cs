using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class MarketplaceAgentCreateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<MarketplaceAgent, MarketplaceAgentCreateResult>();
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<MarketplaceAgent, MarketplaceAgentCreateResult>();
        }
    }
}


