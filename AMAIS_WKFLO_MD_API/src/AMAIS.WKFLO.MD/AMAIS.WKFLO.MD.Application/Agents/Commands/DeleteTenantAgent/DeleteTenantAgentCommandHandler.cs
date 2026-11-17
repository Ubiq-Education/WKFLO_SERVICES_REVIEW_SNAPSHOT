using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent
{
    public class DeleteTenantAgentCommandHandler : IRequestHandler<DeleteTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public DeleteTenantAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(DeleteTenantAgentCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents.Where(m => m.Id == command.Id).FirstOrDefaultAsync();

            if (agent == null)
            {
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            agent.IsDeleted = true;

            // Cascade the soft-delete to the linked published agent (the entity the chat widget actually
            // serves from), matched via CreatedByTenantAgentId — see PublishTenantAgentCommandHandler.
            var publishedAgent = await _db.PublishedTenantAgents
                .Where(m => m.CreatedByTenantAgentId == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (publishedAgent != null)
            {
                publishedAgent.IsDeleted = true;
            }

            // Deliberately does NOT touch EntityTag rows. Deletes/manual DB edits that bypass this handler
            // would leave orphaned tag rows regardless of a delete-on-write approach here, so liveness is
            // instead enforced at read time in ListEntitiesByTagQueryHandler, which filters out tag rows
            // whose target entity is no longer live — correct regardless of how/when it became dead.

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantAgentDeleteResult>(_mapper.Map<TenantAgentDeleteResult>(agent));
        }
    }
}
