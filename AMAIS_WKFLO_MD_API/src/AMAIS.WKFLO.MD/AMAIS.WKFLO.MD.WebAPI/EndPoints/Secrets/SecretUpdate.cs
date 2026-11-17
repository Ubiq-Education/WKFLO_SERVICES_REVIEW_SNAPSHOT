using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.WebAPI.EndPoints.Secrets
{
    [ApiController]
    public class SecretUpdate : BaseAsyncEndpoint<int, UpdateSecretCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public SecretUpdate(IWkfloMediator mediator, IMasterDataDbContext db, IWkfloMapper mapper) : base(mediator)
        {
            _db = db;
            _mapper = mapper;
        }

        [Authorize]
        [HttpPut("/api/v1/secret/{id}")]
        [Authorize(Policy = AuthorizationPolicies.ApiAccess)]
        [Tags("Main Area", "Secret")]
        [EndpointName("Secret.Update")]
        [EndpointSummary("Update a Secret")]
        [EndpointDescription("Update a Secret")]
        [ProducesResponseType(typeof(SuccessResult<SecretUpdateResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(InvalidResult), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(UnexpectedResult), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(Common.Models.Results.NotFoundResult), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(Common.Models.Results.UnauthorizedResult), StatusCodes.Status401Unauthorized)]
        public override async Task<ActionResult<Result>> HandleAsync([FromRoute] int id, UpdateSecretCommand command, CancellationToken cancellationToken = default)
        {
            command.Id = id;

            var secret = await _db.Secrets
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var updateCommand = new UpdateSecretCommand();
            if (secret != null)
            {
                _mapper.Map(secret, updateCommand);
            }

            _mapper.Map(command, updateCommand);

            return AmaisWkfloResult(await Mediator.Send(updateCommand, cancellationToken));
        }
    }
}
