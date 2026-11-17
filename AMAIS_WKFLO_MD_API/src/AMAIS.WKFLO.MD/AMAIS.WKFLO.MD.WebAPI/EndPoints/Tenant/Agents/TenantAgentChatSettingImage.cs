using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UploadAgentChatIcon;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Agents
{
    [ApiController]
    public class TenantAgentChatSettingImage : BaseAsyncEndpoint<int, Result>
    {
        public TenantAgentChatSettingImage(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/tenant/agent/{agentId}/chat-setting/image")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Agent", "Chat Setting")]
        [EndpointName("AgentChatSetting.UploadImage")]
        [EndpointSummary("Upload a Chat Icon Image")]
        [EndpointDescription("Validates, resizes and uploads a chat icon image (bubble/chatbot icon) for a tenant agent to blob storage. Does not persist the resulting path onto the agent's chat setting — the caller (CMS/wkflo_ui) is responsible for saving it via its own Save flow.")]
        [ProducesResponseType(typeof(SuccessResult<AgentChatIconUploadResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [RequestFormLimits(MultipartBodyLengthLimit = 5_242_880)] // 5 MB limit
        [RequestSizeLimit(5_242_880)] // 5 MB limit
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int agentId, CancellationToken cancellationToken = default)
        {
            if (!Request.HasFormContentType || !Request.Form.Files.Any())
            {
                return AmaisWkfloResult(new InvalidResult("Uploader", "No file was uploaded."));
            }

            var file = Request.Form.Files[0];
            if (file.Length == 0)
            {
                return AmaisWkfloResult(new InvalidResult("Uploader", "No file was uploaded."));
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            var command = new UploadAgentChatIconCommand
            {
                Id = agentId,
                Content = memoryStream,
                FileName = file.FileName,
                ContentType = file.ContentType
            };

            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
