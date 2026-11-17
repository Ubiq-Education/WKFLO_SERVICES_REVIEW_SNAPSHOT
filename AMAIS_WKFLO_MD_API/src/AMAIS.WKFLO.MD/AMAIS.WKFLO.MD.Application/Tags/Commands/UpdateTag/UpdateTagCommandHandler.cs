using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.UpdateTag
{
    public class UpdateTagCommandHandler : IRequestHandler<UpdateTagCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateTagCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateTagCommand command, CancellationToken cancellationToken)
        {
            var tag = await _db.Tags
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (tag == null)
                return new NotFoundResult(nameof(Tag), ErrorConst.TagNotFound);

            _mapper.Map(command, tag);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TagUpdateResult>(_mapper.Map<TagUpdateResult>(tag));
        }
    }
}
