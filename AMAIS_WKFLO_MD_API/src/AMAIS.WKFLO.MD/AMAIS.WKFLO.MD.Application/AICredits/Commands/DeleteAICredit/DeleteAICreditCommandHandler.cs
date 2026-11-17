using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AICredits.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AICredits.Commands.DeleteAICredit
{
    public class DeleteAICreditCommandHandler : IRequestHandler<DeleteAICreditCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteAICreditCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteAICreditCommand command, CancellationToken cancellationToken)
        {
            var aiCredit = await _db.AICredits.Where(m => m.Id == command.Id).FirstOrDefaultAsync(cancellationToken);

            if (aiCredit == null)
            {
                return new NotFoundResult(nameof(AICredit), ErrorConst.AICreditNotFound);
            }

            aiCredit.IsDeleted = true;

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AICreditDeleteResult>(_mapper.Map<AICreditDeleteResult>(aiCredit));
        }
    }
}
