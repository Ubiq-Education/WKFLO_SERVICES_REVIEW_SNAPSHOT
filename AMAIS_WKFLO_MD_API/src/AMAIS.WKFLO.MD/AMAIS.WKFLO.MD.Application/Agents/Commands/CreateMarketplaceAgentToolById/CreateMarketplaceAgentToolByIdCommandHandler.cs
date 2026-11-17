using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgentToolById
{
    public class CreateMarketplaceAgentToolByIdCommandHandler : IRequestHandler<CreateMarketplaceAgentToolByIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;

        public CreateMarketplaceAgentToolByIdCommandHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(CreateMarketplaceAgentToolByIdCommand command, CancellationToken cancellationToken)
        {
            await _db.AgentTools.AddAsync(new AgentTool() { AgentId = command.Id, ToolId = command.ToolId }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceAgentToolCreateResult>(new MarketplaceAgentToolCreateResult() { AgentId = command.ToolId, ToolId = command.ToolId });
        }
    }
}
