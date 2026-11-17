using AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitPublicAgentContactForm;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentContactFormSubmit : BaseAsyncEndpoint<string, SubmitPublicAgentContactFormCommand, Result>
    {
        public PublicAgentContactFormSubmit(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOrDatabase)]
        [HttpPost("/api/v1/agent/{shareId}/contact-form/submit")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.ContactFormSubmit")]
        [EndpointSummary("Submit contact form data for a Public Agent")]
        [EndpointDescription("Registers the contact form submission in DXP for the given public agent session.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, SubmitPublicAgentContactFormCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
