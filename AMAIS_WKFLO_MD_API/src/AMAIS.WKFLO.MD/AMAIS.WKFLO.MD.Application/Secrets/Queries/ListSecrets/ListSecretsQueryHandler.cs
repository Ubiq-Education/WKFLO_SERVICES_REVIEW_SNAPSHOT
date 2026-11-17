using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Secrets.Queries.ListSecrets
{
    public class ListSecretsQueryHandler : IRequestHandler<ListSecretsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListSecretsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListSecretsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.Secrets.AsNoTracking()
                .ToListAsync(cancellationToken);

            return result == null ? new NotFoundResult() : (Result)new SuccessResult<List<SecretGetResult>>(_mapper.Map<List<SecretGetResult>>(result));
        }
    }
}
