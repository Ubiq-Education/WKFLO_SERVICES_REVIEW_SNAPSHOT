using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Tags.Models
{
    public class TagDeleteResult : IMap
    {
        public int Id { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<Tag, TagDeleteResult>();
        }
    }
}
