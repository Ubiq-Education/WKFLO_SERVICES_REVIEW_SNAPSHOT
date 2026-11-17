using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById
{
    public class GetAIModelByIdQuery : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }
    }
}

