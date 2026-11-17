using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Secrets.Queries.ListAvailableSecrets
{
    public class ListAvailableSecretsQueryHandler : IRequestHandler<ListAvailableSecretsQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public ListAvailableSecretsQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(ListAvailableSecretsQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.Secrets.AsNoTracking().Where(s => s.IsActive && !s.AIModels.Any())
                .ToListAsync(cancellationToken);

            return result == null ? new NotFoundResult() : (Result)new SuccessResult<List<SecretGetResult>>(_mapper.Map<List<SecretGetResult>>(result));
        }
    }
}
