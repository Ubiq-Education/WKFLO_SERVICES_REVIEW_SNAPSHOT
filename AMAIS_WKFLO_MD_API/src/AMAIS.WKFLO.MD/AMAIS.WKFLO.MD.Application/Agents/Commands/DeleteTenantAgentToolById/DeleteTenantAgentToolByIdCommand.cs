using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgentToolById
{
    public class DeleteTenantAgentToolByIdCommand : IWkfloRequest<Result>
    {
        public int Id { get; set; }
        public int ToolId { get; set; }
    }
}

