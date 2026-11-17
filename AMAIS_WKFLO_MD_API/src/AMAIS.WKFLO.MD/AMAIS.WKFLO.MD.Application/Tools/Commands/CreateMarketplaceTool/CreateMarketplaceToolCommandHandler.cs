using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CreateMarketplaceTool
{
    public class CreateMarketplaceToolCommandHandler : IRequestHandler<CreateMarketplaceToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly IMcpTokenProtector _protector;

        public CreateMarketplaceToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext, IMcpTokenProtector protector)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _protector = protector;
        }

        public async ValueTask<Result> Handle(CreateMarketplaceToolCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = _mapper.Map<MarketplaceTool>(command);

            // US 7849: same client-secret contract as tenant tools — never persisted in plaintext.
            var secretMerge = McpClientSecretHelper.MergeOnWrite(_protector, tenantTool.Type, tenantTool.Config, previousConfigJson: null);
            if (secretMerge.IsInvalidConfig)
                return new InvalidResult(nameof(MarketplaceTool.Config), ErrorConst.ToolMcpConfigInvalid);
            if (secretMerge.IsClientSecretWithoutClientId)
                return new InvalidResult(nameof(MarketplaceTool.Config), ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            tenantTool.Config = secretMerge.Config;

            var tenant = await TryGetTenantAsync(_tenantContext.TenantName);
            tenantTool.Tenant = tenant;

            await _db.MarketplaceTools.AddAsync(tenantTool, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceToolCreateResult>(_mapper.Map<MarketplaceToolCreateResult>(tenantTool));
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
