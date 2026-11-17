using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StoreMessageTenantAgentById
{
    public class StoreMessageTenantAgentByIdCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public StoreMessageTenantAgentInputCommand Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }
    }

    public class StoreMessageTenantAgentInputCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public string Type { get; set; } = string.Empty; // "user", "assistant", "tool"
    }
}
