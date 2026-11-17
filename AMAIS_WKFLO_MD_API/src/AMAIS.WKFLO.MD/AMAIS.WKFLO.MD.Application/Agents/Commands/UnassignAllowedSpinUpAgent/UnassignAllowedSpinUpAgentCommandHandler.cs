using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UnassignAllowedSpinUpAgent
{
    public class UnassignAllowedSpinUpAgentCommandHandler : IRequestHandler<UnassignAllowedSpinUpAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;

        public UnassignAllowedSpinUpAgentCommandHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(UnassignAllowedSpinUpAgentCommand command, CancellationToken cancellationToken)
        {
            var entry = await _db.AgentAllowedSpinUpAgents
                .Where(m => m.AgentId == command.Id && m.AllowedSpinUpAgentId == command.AllowedSpinUpAgentId)
                .FirstOrDefaultAsync(cancellationToken);

            if (entry == null)
                return new NotFoundResult(nameof(AgentAllowedSpinUpAgent), ErrorConst.AllowedSpinUpAgentAssignmentNotFound);

            _db.AgentAllowedSpinUpAgents.Remove(entry);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AllowedSpinUpAgentResult>(new AllowedSpinUpAgentResult
            {
                AgentId = command.Id,
                AllowedSpinUpAgentId = command.AllowedSpinUpAgentId
            });
        }
    }
}
