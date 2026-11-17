using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.RateMarketplaceAgent
{
    public class RateMarketplaceAgentCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, 5, ErrorMessage = ErrorConst.AgentRatingMustBeBetween1And5)]
        public int Rating { get; set; }
    }
}
