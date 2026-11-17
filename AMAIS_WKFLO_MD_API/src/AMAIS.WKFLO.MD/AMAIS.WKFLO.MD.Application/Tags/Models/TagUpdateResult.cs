using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tags.Models
{
    public class TagUpdateResult : IMap
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<Tag, TagUpdateResult>();
        }
    }
}
