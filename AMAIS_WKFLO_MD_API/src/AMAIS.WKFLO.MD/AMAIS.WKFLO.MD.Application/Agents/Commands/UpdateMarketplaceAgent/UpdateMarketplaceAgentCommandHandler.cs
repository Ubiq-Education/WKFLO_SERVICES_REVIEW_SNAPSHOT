using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent
{
    public class UpdateMarketplaceAgentCommandHandler : IRequestHandler<UpdateMarketplaceAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateMarketplaceAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateMarketplaceAgentCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.MarketplaceAgents
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
            {
                return new NotFoundResult(nameof(MarketplaceAgent), ErrorConst.AgentNotFound);
            }

            _mapper.Map(command, agent);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceAgentUpdateResult>(_mapper.Map<MarketplaceAgentUpdateResult>(agent));
        }
    }
}
