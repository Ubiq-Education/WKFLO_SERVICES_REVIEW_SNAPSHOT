using AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitContactForm;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentContactFormSubmit : BaseAsyncEndpoint<int, SubmitContactFormCommand, Result>
    {
        private readonly TenantContext _tenantContext;

        public TenantAgentContactFormSubmit(IWkfloMediator mediator, TenantContext tenantContext) : base(mediator)
        {
            _tenantContext = tenantContext;
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{id}/contact-form/submit")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.ContactFormSubmit")]
        [EndpointSummary("Submit contact form data for a Tenant Agent")]
        [EndpointDescription("Registers the contact form submission in DXP for the given agent session.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, SubmitContactFormCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
