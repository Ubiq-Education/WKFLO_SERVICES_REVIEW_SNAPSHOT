using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent
{
    public class UpdateTenantAgentCommandHandler : IRequestHandler<UpdateTenantAgentCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;

        public UpdateTenantAgentCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async ValueTask<Result> Handle(UpdateTenantAgentCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.TenantAgents
                .Include(m => m.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(m => m.Id == command.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
            {
                return new NotFoundResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            command.ResponseTone ??= agent.ResponseTone ?? "Friendly";
            _mapper.Map(command, agent);

            // null = unchanged (callers unaware of the field, e.g. older clients, must not wipe it);
            // "" / whitespace = clear; anything else replaces. Ignored by the Mapster config above.
            if (command.AdditionalInstructions != null)
            {
                agent.AdditionalInstructions = RunnableAgent.NormalizeAdditionalInstructions(command.AdditionalInstructions);
            }

            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TenantAgentUpdateResult>(_mapper.Map<TenantAgentUpdateResult>(agent));
        }
    }
}
