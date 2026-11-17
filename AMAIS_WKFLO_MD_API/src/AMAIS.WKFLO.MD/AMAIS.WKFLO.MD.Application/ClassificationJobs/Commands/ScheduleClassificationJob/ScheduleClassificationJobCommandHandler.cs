using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors;
using Dapr.Actors;
using Dapr.Actors.Client;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Commands.ScheduleClassificationJob
{
    public class ScheduleClassificationJobCommandHandler : IRequestHandler<ScheduleClassificationJobCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly IActorProxyFactory _actorProxyFactory;
        private readonly ILogger<ScheduleClassificationJobCommandHandler> _logger;

        public ScheduleClassificationJobCommandHandler(
            IMasterDataDbContext db,
            TenantContext tenantContext,
            IActorProxyFactory actorProxyFactory,
            ILogger<ScheduleClassificationJobCommandHandler> logger)
        {
            _db = db;
            _tenantContext = tenantContext;
            _actorProxyFactory = actorProxyFactory;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(ScheduleClassificationJobCommand command, CancellationToken cancellationToken)
        {
            var agent = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == command.AgentId && a.Tenant.Name == _tenantContext.TenantName)
                .Select(a => new { a.Id, a.TenantId })
                .FirstOrDefaultAsync(cancellationToken);

            if (agent == null)
                return new NotFoundResult(nameof(Agent), "Agent not found for this tenant");

            try
            {
                var actorId = new ActorId($"{agent.TenantId}-{agent.Id}");
                var actorProxy = _actorProxyFactory.CreateActorProxy<IClassificationJobSchedulerActor>(
                    actorId, nameof(ClassificationJobSchedulerActor));

                await actorProxy.NotifyTopicConfigChangedAsync();
                return new NoContentResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to schedule classification job for agent {AgentId}", command.AgentId);
                return new UnexpectedResult();
            }
        }
    }
}
