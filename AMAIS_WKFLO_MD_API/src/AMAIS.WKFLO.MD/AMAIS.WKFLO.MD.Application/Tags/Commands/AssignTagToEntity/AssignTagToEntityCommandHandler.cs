using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.AssignTagToEntity
{
    public class AssignTagToEntityCommandHandler : IRequestHandler<AssignTagToEntityCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public AssignTagToEntityCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(AssignTagToEntityCommand command, CancellationToken cancellationToken)
        {
            var alreadyAssigned = await _db.EntityTags
                .AnyAsync(m => m.TagId == command.Id && m.EntityType == command.EntityType && m.EntityId == command.EntityId, cancellationToken);

            if (alreadyAssigned)
                return new InvalidResult(nameof(EntityTag), ErrorConst.TagAlreadyAssigned);

            var entityTag = new EntityTag
            {
                TagId = command.Id,
                EntityType = command.EntityType,
                EntityId = command.EntityId
            };

            await _db.EntityTags.AddAsync(entityTag, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TagAssignResult>(_mapper.Map<TagAssignResult>(entityTag));
        }
    }
}
