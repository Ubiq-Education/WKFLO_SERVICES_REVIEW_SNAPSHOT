using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.DisconnectToolMcpOAuth
{
    public class DisconnectToolMcpOAuthCommandHandler : IRequestHandler<DisconnectToolMcpOAuthCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IMcpOAuthTokenStore _tokenStore;

        public DisconnectToolMcpOAuthCommandHandler(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext, IMcpOAuthTokenStore tokenStore)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _tokenStore = tokenStore;
        }

        public async ValueTask<Result> Handle(DisconnectToolMcpOAuthCommand command, CancellationToken cancellationToken)
        {
            var tool = await _db.TenantTools
                .Where(m => m.McpOAuthConnectionId == command.Id && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);

            // Shared-vs-per-user is a fixed, mutually exclusive choice per tool (see McpServerConfig.OAuthScope):
            // for a Shared tool (e.g. Humantic) this deletes the one connection-wide row — if this tool has a
            // published copy, disconnecting here disconnects it too (and vice versa), since there's genuinely
            // one OAuth grant, not two. For a PerUser tool (e.g. Canva) this deletes only the calling user's
            // own row, leaving every other user at this tenant connected.
            try
            {
                var requestingUserId = McpOAuthScopeResolver.ResolveRequestingUserId(tool.Config, _userContext.Guid);
                await _tokenStore.DeleteAsync(command.Id, requestingUserId, cancellationToken);
            }
            catch (McpOAuthPerUserIdentityUnavailableException)
            {
                // No resolvable identity (should never happen behind [Authorize] — see
                // McpOAuthScopeResolver's remarks) means there is nothing this caller could have connected in
                // the first place; matches DeleteAsync's own no-op-on-missing-row behavior rather than
                // falling back to deleting whatever Shared row might exist for this connection.
            }

            return new SuccessResult<ToolMcpOAuthDisconnectResult>(new ToolMcpOAuthDisconnectResult { Id = command.Id });
        }
    }
}
