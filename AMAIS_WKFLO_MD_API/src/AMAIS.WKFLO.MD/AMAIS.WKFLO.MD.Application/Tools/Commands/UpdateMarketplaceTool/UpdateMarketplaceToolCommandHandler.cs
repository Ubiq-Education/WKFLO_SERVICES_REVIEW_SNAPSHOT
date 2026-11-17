using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool
{
    public class UpdateMarketplaceToolCommandHandler : IRequestHandler<UpdateMarketplaceToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly IMcpTokenProtector _protector;

        public UpdateMarketplaceToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, IMcpTokenProtector protector)
        {
            _db = db;
            _mapper = mapper;
            _protector = protector;
        }

        public async ValueTask<Result> Handle(UpdateMarketplaceToolCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = await _db.MarketplaceTools
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantTool == null)
            {
                return new NotFoundResult(nameof(MarketplaceTool), ErrorConst.ToolNotFound);
            }

            var previousType = tenantTool.Type;
            var previousConfig = tenantTool.Config;

            _mapper.Map(command, tenantTool);

            // US 7849: same client-secret contract as tenant tools. Marketplace tools never hold OAuth tokens
            // (installing one clears its OAuth credentials), so there is nothing to invalidate here.
            var secretMerge = McpClientSecretHelper.MergeOnWrite(
                _protector, tenantTool.Type, tenantTool.Config, previousType == AMAIS.WKFLO.Common.Models.CommonConsts.CommonToolTypeConsts.MCP ? previousConfig : null);
            if (secretMerge.IsInvalidConfig)
                return new InvalidResult(nameof(MarketplaceTool.Config), ErrorConst.ToolMcpConfigInvalid);
            if (secretMerge.IsClientSecretWithoutClientId)
                return new InvalidResult(nameof(MarketplaceTool.Config), ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            tenantTool.Config = secretMerge.Config;

            // A tool that stops being MCP keeps no MCP credentials (see UpdateTenantToolCommandHandler).
            if (previousType == AMAIS.WKFLO.Common.Models.CommonConsts.CommonToolTypeConsts.MCP &&
                tenantTool.Type != AMAIS.WKFLO.Common.Models.CommonConsts.CommonToolTypeConsts.MCP)
                tenantTool.Config = McpClientSecretHelper.StripCredentials(tenantTool.Config);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<MarketplaceToolUpdateResult>(_mapper.Map<MarketplaceToolUpdateResult>(tenantTool));
        }
    }
}
