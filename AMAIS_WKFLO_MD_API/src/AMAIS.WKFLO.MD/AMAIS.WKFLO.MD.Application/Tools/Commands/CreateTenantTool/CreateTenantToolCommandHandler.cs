using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Services;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool
{
    public class CreateTenantToolCommandHandler : IRequestHandler<CreateTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly McpOAuthSettings _mcpOAuthSettings;
        private readonly IMcpTokenProtector _protector;

        public CreateTenantToolCommandHandler(
            IMasterDataDbContext db,
            IWkfloMapper mapper,
            TenantContext tenantContext,
            IOptions<McpOAuthSettings> mcpOAuthSettings,
            IMcpTokenProtector protector)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _mcpOAuthSettings = mcpOAuthSettings.Value;
            _protector = protector;
        }

        public async ValueTask<Result> Handle(CreateTenantToolCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = _mapper.Map<TenantTool>(command);
            tenantTool.PermissionsEnabled = command.PermissionsEnabled ?? false;

            // US 7849: encrypt a posted OAuth client secret and strip the UI's write-only flags.
            var secretMerge = McpClientSecretHelper.MergeOnWrite(_protector, tenantTool.Type, tenantTool.Config, previousConfigJson: null);
            if (secretMerge.IsInvalidConfig)
                return new InvalidResult(nameof(TenantTool.Config), ErrorConst.ToolMcpConfigInvalid);
            if (secretMerge.IsClientSecretWithoutClientId)
                return new InvalidResult(nameof(TenantTool.Config), ErrorConst.ToolMcpOAuthClientSecretRequiresClientId);
            tenantTool.Config = secretMerge.Config;

            var tenant = await TryGetTenantAsync(_tenantContext.TenantName);
            tenantTool.Tenant = tenant;

            McpOAuthConnectionIdProvisioner.EnsureProvisioned(tenantTool, _mcpOAuthSettings);

            await _db.TenantTools.AddAsync(tenantTool, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantToolCreateResult>(_mapper.Map<TenantToolCreateResult>(tenantTool));
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
