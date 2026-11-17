using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.AICredits.Queries.GetAICreditByAIModelId
{
    public class GetAICreditByAIModelIdQuery : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int AIModelId { get; set; }
    }
}
