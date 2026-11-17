using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgentToolById
{
    public class DeleteMarketplaceAgentToolByIdCommandHandler : IRequestHandler<DeleteMarketplaceAgentToolByIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;

        public DeleteMarketplaceAgentToolByIdCommandHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(DeleteMarketplaceAgentToolByIdCommand command, CancellationToken cancellationToken)
        {
            _db.AgentTools.Remove(new AgentTool() { AgentId = command.Id, ToolId = command.ToolId });
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<bool>(true);
        }
    }
}
