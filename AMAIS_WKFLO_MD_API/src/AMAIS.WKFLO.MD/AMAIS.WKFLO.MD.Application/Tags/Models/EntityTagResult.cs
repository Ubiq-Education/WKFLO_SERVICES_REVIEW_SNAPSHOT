using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tags.Models
{
    public class EntityTagResult : IMap
    {
        public string EntityType { get; set; }
        public int EntityId { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<EntityTag, EntityTagResult>();
        }
    }
}
