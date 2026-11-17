using AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentContactExists;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentContactFormCheck : BaseAsyncEndpoint<string, string, Result>
    {
        public PublicAgentContactFormCheck(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOrDatabase)]
        [HttpGet("/api/v1/agent/{shareId}/contact-form/check/{subjectId}")]
        [ProducesResponseType(typeof(SuccessResult<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.ContactFormCheck")]
        [EndpointSummary("Check if a contact already exists for a Public Agent")]
        [EndpointDescription("Returns true if the subject ID is already registered as a contact in DXP for this agent's tenant.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, [FromRoute] string subjectId, CancellationToken cancellationToken = default)
        {
            return AmaisWkfloResult(await Mediator.Send(new CheckPublicAgentContactExistsQuery
            {
                ShareId = shareId,
                SubjectId = subjectId
            }, cancellationToken));
        }
    }
}
