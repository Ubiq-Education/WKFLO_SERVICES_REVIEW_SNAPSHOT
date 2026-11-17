using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents
{
    public class TenantAgentToolApprovalDecide : BaseAsyncEndpoint<int, DecideTenantToolApprovalCommand, Result>
    {
        public TenantAgentToolApprovalDecide(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{agentId}/tool-approval/{approvalId}")]
        [ProducesResponseType(typeof(SuccessResult<ToolApprovalDecisionResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.ConflictResult), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(GoneResult), StatusCodes.Status410Gone)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [Tags("Tenant API", "Agent Chat")]
        [EndpointName("TenantAgent.DecideToolApproval")]
        [EndpointSummary("Answer a tool approval request in the tenant chat")]
        [EndpointDescription("Records AllowOnce, AllowForConversation or Deny for a pending tool approval of this agent and session, then runs (or refuses) the stored tool call and resumes the conversation in the background. Single-use: 409 when already decided, 410 when expired.")]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, DecideTenantToolApprovalCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = agentId;
            command.ApprovalId = RouteData.Values["approvalId"]?.ToString();
            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
