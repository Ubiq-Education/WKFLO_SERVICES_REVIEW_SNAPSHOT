using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.MD.Application.ClassificationJobs.Settings;
using Dapr.Actors.Runtime;
using Dapr.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Actors
{
    public class ClassificationJobSchedulerActor : Actor, IClassificationJobSchedulerActor, IRemindable
    {
        private const string ReminderName = "enqueue";

        private readonly IServiceProvider _serviceProvider;
        private readonly DaprClient _daprClient;
        private readonly ClassificationJobSettings _settings;
        private readonly ILogger<ClassificationJobSchedulerActor> _logger;

        public ClassificationJobSchedulerActor(
            ActorHost host,
            IServiceProvider serviceProvider,
            DaprClient daprClient,
            IOptions<ClassificationJobSettings> settings,
            ILogger<ClassificationJobSchedulerActor> logger)
            : base(host)
        {
            _serviceProvider = serviceProvider;
            _daprClient = daprClient;
            _settings = settings.Value;
            _logger = logger;
        }

        private (int tenantId, int agentId) ParseActorId()
        {
            var parts = Id.GetId().Split('-');
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }

        public async Task NotifyTopicConfigChangedAsync()
        {
            var (tenantId, agentId) = ParseActorId();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<AMAIS.WKFLO.Common.Interfaces.Providers.IDateTimeProvider>();

            var now = dateTimeProvider.DateTimeOffsetUtcNow();

            var pendingJob = await db.ClassificationJobs
                .Where(j => j.TenantId == tenantId && j.AgentId == agentId
                    && j.JobType == CommonJobTypeConsts.TopicClassification
                    && j.QueuedAt == null)
                .OrderByDescending(j => j.PreparedAt)
                .FirstOrDefaultAsync();

            if (pendingJob != null)
            {
                pendingJob.PreparedAt = now;
                await db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("ClassificationJob {JobId} PreparedAt reset for tenant {TenantId} agent {AgentId}", pendingJob.Id, tenantId, agentId);
            }
            else
            {
                var newJob = new ClassificationJob
                {
                    TenantId = tenantId,
                    AgentId = agentId,
                    JobType = CommonJobTypeConsts.TopicClassification,
                    PreparedAt = now
                };
                db.ClassificationJobs.Add(newJob);
                await db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("ClassificationJob created for tenant {TenantId} agent {AgentId}", tenantId, agentId);
            }

            await RegisterReminderAsync(ReminderName, null, TimeSpan.FromHours(_settings.DelayHours), TimeSpan.FromMilliseconds(-1));
        }

        public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            if (reminderName != ReminderName) return;

            var (tenantId, agentId) = ParseActorId();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IMasterDataDbContext>();
            var dateTimeProvider = scope.ServiceProvider.GetRequiredService<AMAIS.WKFLO.Common.Interfaces.Providers.IDateTimeProvider>();

            var now = dateTimeProvider.DateTimeOffsetUtcNow();

            var pendingJob = await db.ClassificationJobs
                .Where(j => j.TenantId == tenantId && j.AgentId == agentId
                    && j.JobType == CommonJobTypeConsts.TopicClassification
                    && j.QueuedAt == null)
                .OrderByDescending(j => j.PreparedAt)
                .FirstOrDefaultAsync();

            if (pendingJob == null)
            {
                _logger.LogInformation("No pending ClassificationJob found for tenant {TenantId} agent {AgentId} — nothing to enqueue", tenantId, agentId);
                return;
            }

            // Check 24h cooldown from last finished job
            var lastFinished = await db.ClassificationJobs
                .Where(j => j.TenantId == tenantId && j.AgentId == agentId
                    && j.JobType == CommonJobTypeConsts.TopicClassification
                    && j.FinishedAt != null)
                .OrderByDescending(j => j.FinishedAt)
                .FirstOrDefaultAsync();

            if (lastFinished?.FinishedAt != null)
            {
                var elapsed = now - lastFinished.FinishedAt.Value;
                var cooldown = TimeSpan.FromHours(_settings.CooldownHours);
                if (elapsed < cooldown)
                {
                    var remaining = cooldown - elapsed;
                    _logger.LogInformation("ClassificationJob cooldown active for tenant {TenantId} agent {AgentId} — rescheduling in {Remaining}", tenantId, agentId, remaining);
                    await RegisterReminderAsync(ReminderName, null, remaining, TimeSpan.FromMilliseconds(-1));
                    return;
                }
            }

            pendingJob.QueuedAt = now;
            await db.SaveChangesAsync(CancellationToken.None);

            await _daprClient.PublishEventAsync(
                CommonJobPubSubConsts.PubSubName,
                CommonJobPubSubConsts.JobQueuedTopic,
                new ClassificationJobQueuedMessage(pendingJob.Id));

            _logger.LogInformation("ClassificationJob {JobId} enqueued for tenant {TenantId} agent {AgentId}", pendingJob.Id, tenantId, agentId);
        }
    }
}
