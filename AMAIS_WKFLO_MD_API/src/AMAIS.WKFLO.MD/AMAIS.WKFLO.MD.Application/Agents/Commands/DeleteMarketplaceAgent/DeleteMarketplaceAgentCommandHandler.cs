using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent
{
    public class DeleteMarketplaceAgentCommandHandler : IRequestHandler<DeleteMarketplaceAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteMarketplaceAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteMarketplaceAgentCommand command, CancellationToken cancellationToken)
        {
            var marketplaceAgent = await _db.MarketplaceAgents.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (marketplaceAgent == null)
            {
                return new NotFoundResult(nameof(MarketplaceAgent), ErrorConst.AgentNotFound);
            }

            marketplaceAgent.IsDeleted = true;

            var publishedTenantAgent = await _db.PublishedTenantAgents.Where(m => m.Id == marketplaceAgent.SourcePublishedAgentId).FirstOrDefaultAsync();
            if (publishedTenantAgent != null)
            {
                publishedTenantAgent.MarketplaceAgentId = null;
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceAgentDeleteResult>(_mapper.Map<MarketplaceAgentDeleteResult>(marketplaceAgent));
        }
    }
}
