using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    public class TenantToolCreate : BaseAsyncEndpoint<CreateTenantToolCommand, Result>
    {
        public TenantToolCreate(IWkfloMediator mediator) : base(mediator)
        {

        }

        [Authorize]
        [HttpPost("/api/v1/tenant/tool")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.Create")]
        [EndpointSummary("Create a Tenant Tool")]
        [EndpointDescription("Create a Tenant Tool")]
        [ProducesResponseType(typeof(SuccessResult<TenantToolCreateResult>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync(CreateTenantToolCommand command, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
