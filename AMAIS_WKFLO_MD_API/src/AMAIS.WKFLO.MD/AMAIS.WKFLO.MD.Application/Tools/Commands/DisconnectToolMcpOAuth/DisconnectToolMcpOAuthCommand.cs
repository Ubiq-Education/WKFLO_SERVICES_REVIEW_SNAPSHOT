using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.DisconnectToolMcpOAuth
{
    public class DisconnectToolMcpOAuthCommand : IWkfloRequest<Result>, IRequestId<Guid>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public Guid Id { get; set; }
    }
}
