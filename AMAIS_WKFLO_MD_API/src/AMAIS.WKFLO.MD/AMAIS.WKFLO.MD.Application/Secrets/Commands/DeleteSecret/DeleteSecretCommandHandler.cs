using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret
{
    public class DeleteSecretCommandHandler : IRequestHandler<DeleteSecretCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteSecretCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteSecretCommand command, CancellationToken cancellationToken)
        {
            var secret = await _db.Secrets.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (secret == null)
            {
                return new NotFoundResult(nameof(Secret), ErrorConst.SecretNotFound);
            }

            secret.IsDeleted = true;

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<SecretDeleteResult>(_mapper.Map<SecretDeleteResult>(secret));
        }
    }
}
