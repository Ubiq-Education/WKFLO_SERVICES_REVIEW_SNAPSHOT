using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentToolsById
{
    public class GetMarketplaceAgentToolsByIdQueryHandler : IRequestHandler<GetMarketplaceAgentToolsByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetMarketplaceAgentToolsByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetMarketplaceAgentToolsByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.MarketplaceTools.AsNoTracking()
                .Where(m => m.AgentTools.Any(m2 => m2.AgentId == query.Id))
                .ToListAsync(cancellationToken) ?? new List<MarketplaceTool>();

            return new SuccessResult<List<MarketplaceAgentToolGetResult>>(_mapper.Map<List<MarketplaceAgentToolGetResult>>(result));
        }
    }
}
