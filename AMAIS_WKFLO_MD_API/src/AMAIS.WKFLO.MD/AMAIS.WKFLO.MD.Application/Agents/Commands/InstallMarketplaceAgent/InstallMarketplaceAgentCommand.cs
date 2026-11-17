using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.InstallMarketplaceAgent
{
    public class InstallMarketplaceAgentCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int Id { get; set; }
    }
}
