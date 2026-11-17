using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.RateMessagePublicAgentByShareId
{
    public class RateMessagePublicAgentByShareIdCommand : IWkfloRequest<Result>, IRequestShareId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public RateMessagePublicAgentInput Input { get; set; }
    }

    public class RateMessagePublicAgentInput
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string MessageId { get; set; }

        public bool IsThumbsUp { get; set; }
    }
}

