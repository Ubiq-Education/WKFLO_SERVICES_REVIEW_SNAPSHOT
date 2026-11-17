using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AICredits
{
    [ApiController]
    public class AICreditUpdate : BaseAsyncEndpoint<int, UpdateAICreditCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public AICreditUpdate(IWkfloMediator mediator, IMasterDataDbContext db, IWkfloMapper mapper) : base(mediator)
        {
            _db = db;
            _mapper = mapper;
        }

        [Authorize]
        [HttpPut("/api/v1/ai-credit/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "AICredit")]
        [EndpointName("AICredit.Update")]
        [EndpointSummary("Update an AICredit")]
        [EndpointDescription("Update an AICredit")]
        [ProducesResponseType(typeof(SuccessResult<AICreditUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateAICreditCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;

            var aiCredit = await _db.AICredits
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var updateCommand = new UpdateAICreditCommand();
            if (aiCredit != null)
            {
                _mapper.Map(aiCredit, updateCommand);
            }

            _mapper.Map(command, updateCommand);

            return AmaisWkfloResult(await Mediator.Send(updateCommand, cancellationToken));
        }
    }
}
