using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.DeleteAIModel
{
    public class DeleteAIModelCommandHandler : IRequestHandler<DeleteAIModelCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteAIModelCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteAIModelCommand command, CancellationToken cancellationToken)
        {
            var aiModel = await _db.AIModels.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (aiModel == null)
            {
                return new NotFoundResult(nameof(AIModel), ErrorConst.AIModelNotFound);
            }

            aiModel.IsDeleted = true;

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AIModelDeleteResult>(_mapper.Map<AIModelDeleteResult>(aiModel));
        }
    }
}
