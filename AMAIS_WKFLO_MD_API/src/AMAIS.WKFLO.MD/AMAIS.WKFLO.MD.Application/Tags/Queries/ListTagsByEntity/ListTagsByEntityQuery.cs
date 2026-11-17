using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tags.Queries.ListTagsByEntity
{
    public class ListTagsByEntityQuery : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string EntityType { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public int EntityId { get; set; }
    }
}
