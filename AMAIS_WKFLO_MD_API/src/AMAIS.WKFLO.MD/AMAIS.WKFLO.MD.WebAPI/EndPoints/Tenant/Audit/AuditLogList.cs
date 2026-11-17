using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Audit.Models;
using AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Audit
{
    public class AuditLogList : BaseAsyncEndpoint<ListAuditLogQuery, Result>
    {
        public AuditLogList(IWkfloMediator mediator) : base(mediator) { }

        [Authorize]
        [HttpGet("/api/v1/tenant/audit")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Audit")]
        [EndpointName("Audit.List")]
        [EndpointSummary("List Audit Log Entries")]
        [EndpointDescription("Returns a paginated list of audit log entries (Create/Update/Delete actions on MD entities) for the tenant, ordered by most recent first. Supports filtering by entityType, action, actor, and date range. Defaults to page 1 with 15 items per page.")]
        [ProducesResponseType(typeof(SuccessResult<AuditLogListPageResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromQuery] ListAuditLogQuery query, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(query, cancellationToken));
        }
    }
}
