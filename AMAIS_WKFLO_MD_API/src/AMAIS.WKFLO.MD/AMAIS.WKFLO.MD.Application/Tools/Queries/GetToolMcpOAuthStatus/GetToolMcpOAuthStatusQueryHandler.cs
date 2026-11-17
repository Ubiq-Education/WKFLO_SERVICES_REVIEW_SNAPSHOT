using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Queries.GetToolMcpOAuthStatus
{
    public class GetToolMcpOAuthStatusQueryHandler : IRequestHandler<GetToolMcpOAuthStatusQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public GetToolMcpOAuthStatusQueryHandler(IMasterDataDbContext db, TenantContext tenantContext, UserContext userContext)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask<Result> Handle(GetToolMcpOAuthStatusQuery query, CancellationToken cancellationToken)
        {
            var tool = await _db.TenantTools.AsNoTracking()
                .Where(m => m.McpOAuthConnectionId == query.Id && m.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            if (tool == null)
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);

            // Shared-vs-per-user is a fixed, mutually exclusive choice per tool (see McpServerConfig.OAuthScope):
            // for a PerUser tool (e.g. Canva) this reports whether THIS caller has connected, not whether
            // anyone at the tenant has — mirroring StartToolMcpOAuthCommandHandler's resolution. An
            // unresolvable identity (should never happen behind [Authorize], but see
            // McpOAuthScopeResolver's remarks for why this must never silently fall back to the Shared row)
            // reports NotConnected — "as far as we can tell, not connected" is accurate and safe when we
            // genuinely cannot identify the caller.
            Guid? requestingUserId;
            try
            {
                requestingUserId = McpOAuthScopeResolver.ResolveRequestingUserId(tool.Config, _userContext.Guid);
            }
            catch (McpOAuthPerUserIdentityUnavailableException)
            {
                return new SuccessResult<ToolMcpOAuthStatusResult>(new ToolMcpOAuthStatusResult
                {
                    Status = McpOAuthStatus.NotConnected,
                    Reason = null,
                    ConnectedOnUtc = null
                });
            }

            // tool was found by matching McpOAuthConnectionId == query.Id, so it's guaranteed non-null here.
            var token = await _db.ToolOAuthTokens.AsNoTracking()
                .Where(m => m.McpOAuthConnectionId == query.Id && m.UserId == requestingUserId)
                .FirstOrDefaultAsync(cancellationToken);

            // token.LastCheckSucceeded == null means a token was saved but no connection outcome has been
            // recorded yet — treated as healthy/"Connected", matching the pre-existing behavior for that
            // window (before this tri-state existed, a saved token alone meant "Connected").
            var status = token == null
                ? McpOAuthStatus.NotConnected
                : token.LastCheckSucceeded == false
                    ? McpOAuthStatus.ConnectionIssue
                    : McpOAuthStatus.Connected;

            return new SuccessResult<ToolMcpOAuthStatusResult>(new ToolMcpOAuthStatusResult
            {
                Status = status,
                Reason = token?.LastCheckSucceeded == false ? token.LastError : null,
                ConnectedOnUtc = token?.ConnectedOnUtc
            });
        }
    }
}
