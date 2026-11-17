using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateMarketplaceTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Marketplace.Tools
{
    [ApiController]
    public class MarketplaceToolUpdate : BaseAsyncEndpoint<int, UpdateMarketplaceToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public MarketplaceToolUpdate(IWkfloMediator mediator, IMasterDataDbContext db, IWkfloMapper mapper) : base(mediator)
        {
            _db = db;
            _mapper = mapper;
        }

        [Authorize]
        [HttpPut("/api/v1/marketplace/tool/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Marketplace Area", "Tool")]
        [EndpointName("MarketplaceTool.Update")]
        [EndpointSummary("Update a Marketplace Tool")]
        [EndpointDescription("Update a Marketplace Tool")]
        [ProducesResponseType(typeof(SuccessResult<MarketplaceToolUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateMarketplaceToolCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;

            var tool = await _db.MarketplaceTools
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var updateCommand = new UpdateMarketplaceToolCommand();
            if (tool != null)
            {
                _mapper.Map(tool, updateCommand);
            }

            _mapper.Map(command, updateCommand);

            return AmaisWkfloResult(await Mediator.Send(updateCommand, cancellationToken));
        }
    }
}
