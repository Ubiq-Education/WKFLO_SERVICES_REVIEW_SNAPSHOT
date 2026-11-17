using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret
{
    public class UpdateSecretCommandHandler : IRequestHandler<UpdateSecretCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateSecretCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateSecretCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = await _db.Secrets
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantTool == null)
            {
                return new NotFoundResult(nameof(Secret), ErrorConst.SecretNotFound);
            }

            _mapper.Map(command, tenantTool);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<SecretUpdateResult>(_mapper.Map<SecretUpdateResult>(tenantTool));
        }
    }
}
