using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent
{
    public class UnassignAllowedSpinUpAgentCommand : IWkfloRequest<Result>
    {
        public int Id { get; set; }
        public int AllowedSpinUpAgentId { get; set; }
    }
}
