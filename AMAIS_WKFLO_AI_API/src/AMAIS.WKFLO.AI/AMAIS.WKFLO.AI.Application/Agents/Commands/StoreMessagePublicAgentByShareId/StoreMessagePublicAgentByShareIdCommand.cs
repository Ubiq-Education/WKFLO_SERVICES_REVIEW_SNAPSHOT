using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StoreMessagePublicAgentByShareId
{
    public class StoreMessagePublicAgentByShareIdCommand : IWkfloRequest<Result>, IRequestShareId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public StoreMessagePublicAgentInputCommand Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }
    }

    public class StoreMessagePublicAgentInputCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public string Type { get; set; } = string.Empty; // "user", "assistant", "tool"
    }
}
