using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.AIModels
{
    [ApiController]
    public class AIModelUpdate : BaseAsyncEndpoint<int, UpdateAIModelCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public AIModelUpdate(IWkfloMediator mediator, IMasterDataDbContext db, IWkfloMapper mapper) : base(mediator)
        {
            _db = db;
            _mapper = mapper;
        }

        [Authorize]
        [HttpPut("/api/v1/ai-model/{id}")]
        [Tags("Main Area", "AIModel")]
        [EndpointName("AIModel.Update")]
        [EndpointSummary("Update an AIModel")]
        [EndpointDescription("Update an AIModel")]
        [ProducesResponseType(typeof(SuccessResult<AIModelUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateAIModelCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;

            var aimodel = await _db.AIModels
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var updateCommand = new UpdateAIModelCommand();
            if (aimodel != null)
            {
                _mapper.Map(aimodel, updateCommand);
            }

            _mapper.Map(command, updateCommand);

            return AmaisWkfloResult(await Mediator.Send(updateCommand, cancellationToken));
        }
    }
}
