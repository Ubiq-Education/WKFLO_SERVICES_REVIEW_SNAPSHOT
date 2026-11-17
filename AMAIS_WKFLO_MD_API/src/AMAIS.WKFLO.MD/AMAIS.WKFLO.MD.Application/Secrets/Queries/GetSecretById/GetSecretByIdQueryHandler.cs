using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById
{
    public class GetSecretByIdQueryHandler : IRequestHandler<GetSecretByIdQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public GetSecretByIdQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(GetSecretByIdQuery query, CancellationToken cancellationToken)
        {
            var result = await _db.Secrets.AsNoTracking()
                .Where(m => m.Id == query.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return result == null ? new NotFoundResult() : (Result)new SuccessResult<SecretGetResult>(_mapper.Map<SecretGetResult>(result));
        }
    }
}
