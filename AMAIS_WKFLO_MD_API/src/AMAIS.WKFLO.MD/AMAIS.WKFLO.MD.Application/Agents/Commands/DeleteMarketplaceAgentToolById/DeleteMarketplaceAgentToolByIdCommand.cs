using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgentToolById
{
    public class DeleteMarketplaceAgentToolByIdCommand : IWkfloRequest<Result>
    {
        public int Id { get; set; }
        public int ToolId { get; set; }
    }
}

