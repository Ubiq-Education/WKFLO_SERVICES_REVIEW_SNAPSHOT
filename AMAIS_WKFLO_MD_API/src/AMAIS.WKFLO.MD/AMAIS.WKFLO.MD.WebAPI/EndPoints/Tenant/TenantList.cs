using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenants;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantList : BaseAsyncEndpoint<Result>
    {
        public TenantList(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpGet("/api/v1/tenants")]
        [RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
        [Tags("Tenant Area", "Tenant")]
        [EndpointName("Tenant.List")]
        [EndpointSummary("List the Tenants")]
        [EndpointDescription("List the Tenants")]
        [ProducesResponseType(typeof(SuccessResult<List<TenantGetResult>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new ListTenantsQuery(), cancellationToken));
        }
    }
}
