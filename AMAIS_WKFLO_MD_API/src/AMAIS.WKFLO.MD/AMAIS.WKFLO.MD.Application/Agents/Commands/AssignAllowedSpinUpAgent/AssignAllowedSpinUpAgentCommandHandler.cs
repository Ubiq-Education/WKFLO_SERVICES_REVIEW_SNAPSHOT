using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.AssignAllowedSpinUpAgent
{
    public class AssignAllowedSpinUpAgentCommandHandler : IRequestHandler<AssignAllowedSpinUpAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;

        public AssignAllowedSpinUpAgentCommandHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(AssignAllowedSpinUpAgentCommand command, CancellationToken cancellationToken)
        {
            var alreadyAssigned = await _db.AgentAllowedSpinUpAgents
                .AnyAsync(m => m.AgentId == command.Id && m.AllowedSpinUpAgentId == command.AllowedSpinUpAgentId, cancellationToken);

            if (alreadyAssigned)
                return new InvalidResult(nameof(AgentAllowedSpinUpAgent), ErrorConst.AllowedSpinUpAgentAlreadyAssigned);

            await _db.AgentAllowedSpinUpAgents.AddAsync(new AgentAllowedSpinUpAgent
            {
                AgentId = command.Id,
                AllowedSpinUpAgentId = command.AllowedSpinUpAgentId
            }, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<AllowedSpinUpAgentResult>(new AllowedSpinUpAgentResult
            {
                AgentId = command.Id,
                AllowedSpinUpAgentId = command.AllowedSpinUpAgentId
            });
        }
    }
}
