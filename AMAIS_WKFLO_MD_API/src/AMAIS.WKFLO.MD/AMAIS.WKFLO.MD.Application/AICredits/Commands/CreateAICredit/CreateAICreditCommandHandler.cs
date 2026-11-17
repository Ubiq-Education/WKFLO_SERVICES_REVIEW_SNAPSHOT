using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandHandler : IRequestHandler<CreateAICreditCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public CreateAICreditCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(CreateAICreditCommand command, CancellationToken cancellationToken)
        {
            var aiCredit = _mapper.Map<AICredit>(command);

            await _db.AICredits.AddAsync(aiCredit, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AICreditCreateResult>(_mapper.Map<AICreditCreateResult>(aiCredit));
        }
    }
}
