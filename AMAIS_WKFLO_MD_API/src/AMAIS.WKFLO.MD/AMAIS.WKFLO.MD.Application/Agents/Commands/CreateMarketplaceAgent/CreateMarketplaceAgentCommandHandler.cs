using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent
{
    public class CreateMarketplaceAgentCommandHandler : IRequestHandler<CreateMarketplaceAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public CreateMarketplaceAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(CreateMarketplaceAgentCommand command, CancellationToken cancellationToken)
        {
            var agent = _mapper.Map<MarketplaceAgent>(command);

            var tenant = await TryGetTenantAsync(_tenantContext.TenantName);
            agent.Tenant = tenant;

            await _db.MarketplaceAgents.AddAsync(agent, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceAgentCreateResult>(_mapper.Map<MarketplaceAgentCreateResult>(agent));
        }

        protected async Task<Tenant> TryGetTenantAsync(string tenant)
        {
            var tenantDB = await _db.Tenants.Where(m => m.Name == tenant).FirstOrDefaultAsync();
            if (tenantDB == null)
            {
                return new Tenant() { Name = tenant };
            }
            else
            {
                return tenantDB;
            }
        }
    }
}
