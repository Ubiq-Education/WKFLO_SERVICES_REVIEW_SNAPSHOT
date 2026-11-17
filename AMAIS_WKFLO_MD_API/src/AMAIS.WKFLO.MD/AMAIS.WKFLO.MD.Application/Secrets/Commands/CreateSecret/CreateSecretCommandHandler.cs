using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret
{
    public class CreateSecretCommandHandler : IRequestHandler<CreateSecretCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public CreateSecretCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(CreateSecretCommand command, CancellationToken cancellationToken)
        {
            var secret = _mapper.Map<Secret>(command);

            await _db.Secrets.AddAsync(secret, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<SecretCreateResult>(_mapper.Map<SecretCreateResult>(secret));
        }
    }
}
