using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.CreateTag
{
    public class CreateTagCommand : IWkfloRequest<Result>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [MaxLength(20, ErrorMessage = ErrorConst.MaximumLength)]
        public string Color { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<CreateTagCommand, Tag>();
        }
    }
}
