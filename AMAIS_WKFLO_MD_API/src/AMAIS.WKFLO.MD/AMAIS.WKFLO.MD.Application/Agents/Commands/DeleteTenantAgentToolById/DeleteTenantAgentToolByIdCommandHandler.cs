using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgentToolById
{
    public class DeleteTenantAgentToolByIdCommandHandler : IRequestHandler<DeleteTenantAgentToolByIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;

        public DeleteTenantAgentToolByIdCommandHandler(IMasterDataDbContext db)
        {
            _db = db;
        }

        public async ValueTask<Result> Handle(DeleteTenantAgentToolByIdCommand command, CancellationToken cancellationToken)
        {
            _db.AgentTools.Remove(new AgentTool() { AgentId = command.Id, ToolId = command.ToolId });
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<bool>(true);
        }
    }
}
