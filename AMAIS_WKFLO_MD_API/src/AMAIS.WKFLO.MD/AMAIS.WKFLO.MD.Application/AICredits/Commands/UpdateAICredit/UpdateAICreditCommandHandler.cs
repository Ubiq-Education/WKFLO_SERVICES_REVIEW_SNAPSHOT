using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.UpdateAICredit
{
    public class UpdateAICreditCommandHandler : IRequestHandler<UpdateAICreditCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateAICreditCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateAICreditCommand command, CancellationToken cancellationToken)
        {
            var aiCredit = await _db.AICredits
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (aiCredit == null)
            {
                return new NotFoundResult(nameof(AICredit), ErrorConst.AICreditNotFound);
            }

            _mapper.Map(command, aiCredit);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AICreditUpdateResult>(_mapper.Map<AICreditUpdateResult>(aiCredit));
        }
    }
}
