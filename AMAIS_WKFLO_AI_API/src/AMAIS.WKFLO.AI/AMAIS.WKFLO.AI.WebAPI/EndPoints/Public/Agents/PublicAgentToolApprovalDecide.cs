using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents
{
    public class PublicAgentToolApprovalDecide : BaseAsyncEndpoint<string, DecidePublicToolApprovalCommand, Result>
    {
        public PublicAgentToolApprovalDecide(IWkfloMediator mediator) : base(mediator)
        {
        }

        [WebChatAuthorization(AuthorizationMethod.AmaisJwtOrDatabase)]
        [HttpPost("/api/v1/agent/{shareId}/tool-approval/{approvalId}")]
        [ProducesResponseType(typeof(SuccessResult<ToolApprovalDecisionResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.ConflictResult), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(GoneResult), StatusCodes.Status410Gone)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Public API", "Agent Chat")]
        [EndpointName("PublicAgent.DecideToolApproval")]
        [EndpointSummary("Answer a tool approval request in a shared chat")]
        [EndpointDescription("Records AllowOnce, AllowForConversation or Deny for a pending tool approval of the shared agent and session, then runs (or refuses) the stored tool call and resumes the conversation in the background. Single-use: 409 when already decided, 410 when expired.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] string shareId, DecidePublicToolApprovalCommand command, CancellationToken cancellationToken = default)
        {
            command.ShareId = shareId;
            command.ApprovalId = RouteData.Values["approvalId"]?.ToString();
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
