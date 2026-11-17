using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessageTenantAgentById
{
    public class RateMessageTenantAgentByIdCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public RateMessageTenantAgentInput Input { get; set; }
    }

    public class RateMessageTenantAgentInput
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string MessageId { get; set; }

        public bool IsThumbsUp { get; set; }
    }
}

