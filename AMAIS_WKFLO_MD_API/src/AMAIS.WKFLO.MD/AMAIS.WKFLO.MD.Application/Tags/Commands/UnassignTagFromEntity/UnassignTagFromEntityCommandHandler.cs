using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.UnassignTagFromEntity
{
    public class UnassignTagFromEntityCommandHandler : IRequestHandler<UnassignTagFromEntityCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UnassignTagFromEntityCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UnassignTagFromEntityCommand command, CancellationToken cancellationToken)
        {
            var entityTag = await _db.EntityTags
                .Where(m => m.TagId == command.Id && m.EntityType == command.EntityType && m.EntityId == command.EntityId)
                .FirstOrDefaultAsync(cancellationToken);

            if (entityTag == null)
                return new NotFoundResult(nameof(EntityTag), ErrorConst.TagAssignmentNotFound);

            _db.EntityTags.Remove(entityTag);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TagAssignResult>(_mapper.Map<TagAssignResult>(entityTag));
        }
    }
}
