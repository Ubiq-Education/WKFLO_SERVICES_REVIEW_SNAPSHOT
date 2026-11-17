using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.DeleteTenantTool
{
    public class DeleteTenantToolCommandHandler : IRequestHandler<DeleteTenantToolCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteTenantToolCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteTenantToolCommand command, CancellationToken cancellationToken)
        {
            var tenantTool = await _db.TenantTools.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (tenantTool == null)
            {
                return new NotFoundResult(nameof(Tool), ErrorConst.ToolNotFound);
            }

            tenantTool.IsDeleted = true;

            // Cascade the soft-delete to the linked published tool (the entity the chat widget actually
            // serves from), matched via CreatedByTenantToolId — see PublishTenantToolCommandHandler.
            var publishedTool = await _db.PublishedTenantTools
                .Where(m => m.CreatedByTenantToolId == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (publishedTool != null)
            {
                publishedTool.IsDeleted = true;
            }

            // Deliberately does NOT touch EntityTag rows. Deletes/manual DB edits that bypass this handler
            // would leave orphaned tag rows regardless of a delete-on-write approach here, so liveness is
            // instead enforced at read time in ListEntitiesByTagQueryHandler, which filters out tag rows
            // whose target entity is no longer live — correct regardless of how/when it became dead.

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantToolDeleteResult>(_mapper.Map<TenantToolDeleteResult>(tenantTool));
        }
    }
}
