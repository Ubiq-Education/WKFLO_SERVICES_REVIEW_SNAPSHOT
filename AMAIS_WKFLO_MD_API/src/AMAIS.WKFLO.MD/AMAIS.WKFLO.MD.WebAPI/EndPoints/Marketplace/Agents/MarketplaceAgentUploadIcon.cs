using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UploadMarketplaceAgentIcon;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Agents
{
    [ApiController]
    public class MarketplaceAgentUploadIcon : BaseAsyncEndpoint<int, Result>
    {
        public MarketplaceAgentUploadIcon(IWkfloMediator mediator) : base(mediator)
        {
        }

        [Authorize]
        [HttpPost("/api/v1/marketplace/agent/{id}/icon")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Agent")]
        [EndpointName("MarketplaceAgent.UploadIcon")]
        [EndpointSummary("Upload a Marketplace Agent Icon Image")]
        [EndpointDescription("Validates, resizes and uploads an icon image for a Marketplace agent to blob storage. Does not persist the resulting URL onto MarketplaceAgent.IconUrl — the caller (wkflo_ui) is responsible for saving it via MarketplaceAgentUpdate's own Save flow.")]
        [ProducesResponseType(typeof(SuccessResult<MarketplaceAgentIconUploadResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        [RequestFormLimits(MultipartBodyLengthLimit = 5_242_880)] // 5 MB limit
        [RequestSizeLimit(5_242_880)] // 5 MB limit
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, CancellationToken cancellationToken = default)
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

            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = id,
                Content = memoryStream,
                FileName = file.FileName,
                ContentType = file.ContentType
            };

            return AmaisWkfloResult(await Mediator.Send(command, cancellationToken));
        }
    }
}
