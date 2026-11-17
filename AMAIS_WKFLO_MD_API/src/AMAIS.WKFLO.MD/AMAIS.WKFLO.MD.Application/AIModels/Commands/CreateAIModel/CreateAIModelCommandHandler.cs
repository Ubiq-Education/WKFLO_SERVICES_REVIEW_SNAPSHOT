using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.AIModels.Models;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommandHandler : IRequestHandler<CreateAIModelCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public CreateAIModelCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(CreateAIModelCommand command, CancellationToken cancellationToken)
        {
            var aiModel = _mapper.Map<AIModel>(command);

            await _db.AIModels.AddAsync(aiModel, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AIModelCreateResult>(_mapper.Map<AIModelCreateResult>(aiModel));
        }
    }
}
