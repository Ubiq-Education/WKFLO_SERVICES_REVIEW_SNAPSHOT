using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.UpdateTag
{
    public class UpdateTagCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [MaxLength(20, ErrorMessage = ErrorConst.MaximumLength)]
        public string Color { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateTagCommand, Tag>();
            config.NewConfig<Tag, UpdateTagCommand>();
            config.NewConfig<UpdateTagCommand, UpdateTagCommand>()
                .IgnoreNullValues(true);
        }
    }
}
