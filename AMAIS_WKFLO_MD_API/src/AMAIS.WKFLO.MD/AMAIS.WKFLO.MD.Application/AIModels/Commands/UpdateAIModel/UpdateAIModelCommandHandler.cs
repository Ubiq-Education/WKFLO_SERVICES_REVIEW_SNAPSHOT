using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.UpdateAIModel
{
    public class UpdateAIModelCommandHandler : IRequestHandler<UpdateAIModelCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateAIModelCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateAIModelCommand command, CancellationToken cancellationToken)
        {
            var aiModel = await _db.AIModels
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (aiModel == null)
            {
                return new NotFoundResult(nameof(AIModel), ErrorConst.AIModelNotFound);
            }

            _mapper.Map(command, aiModel);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AIModelUpdateResult>(_mapper.Map<AIModelUpdateResult>(aiModel));
        }
    }
}
