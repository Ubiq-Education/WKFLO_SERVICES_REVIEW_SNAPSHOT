using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Tenant.Tools
{
    [ApiController]
    public class TenantToolUpdate : BaseAsyncEndpoint<int, UpdateTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public TenantToolUpdate(IWkfloMediator mediator, IMasterDataDbContext db, IWkfloMapper mapper) : base(mediator)
        {
            _db = db;
            _mapper = mapper;
        }

        [Authorize]
        [HttpPut("/api/v1/tenant/tool/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Tenant Area", "Tool")]
        [EndpointName("TenantTool.Update")]
        [EndpointSummary("Update a Tenant Tool")]
        [EndpointDescription("Update a Tenant Tool")]
        [ProducesResponseType(typeof(SuccessResult<TenantToolUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateTenantToolCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;

            var tool = await _db.TenantTools
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var updateCommand = new UpdateTenantToolCommand();
            if (tool != null)
            {
                _mapper.Map(tool, updateCommand);
            }

            _mapper.Map(command, updateCommand);

            // Bug 7871: the merge above skips nulls, so an explicit "maxToolOutputLength": null (clear the cap)
            // would silently keep the stored value. Apply it when the body actually carried the field; an
            // omitted field (e.g. the tool card's { isActive } toggle) keeps the pre-filled stored cap.
            if (command.MaxToolOutputLengthSpecified)
            {
                updateCommand.MaxToolOutputLength = command.MaxToolOutputLength;
            }

            return AmaisWkfloResult(await Mediator.Send(updateCommand, cancellationToken));
        }
    }
}
