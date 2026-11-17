using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted
{
    public class LogContactFormSubmittedDomainEventHandler : INotificationHandler<ContactFormSubmittedDomainEvent>
    {
        private const string SubjectIdSource = "DXP";
        private const string ActionContactFormSubmit = "ContactFormSubmit";

        private readonly IMasterDataDbContext _db;
        private readonly ILogger<LogContactFormSubmittedDomainEventHandler> _logger;

        public LogContactFormSubmittedDomainEventHandler(IMasterDataDbContext db, ILogger<LogContactFormSubmittedDomainEventHandler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async ValueTask Handle(ContactFormSubmittedDomainEvent notification, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(notification.SubjectId))
                return;

            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == notification.TenantName && !t.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null)
                return;

            int? agentId = notification.AgentId;

            if (agentId == null && !string.IsNullOrEmpty(notification.ShareId))
            {
                agentId = await _db.AgentChatSettings
                    .AsNoTracking()
                    .Where(a => a.ShareId == notification.ShareId && !a.IsDeleted)
                    .Select(a => (int?)a.AgentId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (agentId == null)
            {
                _logger.LogWarning("ContactFormPluginLog: could not resolve AgentId for tenant {TenantName}", notification.TenantName);
                return;
            }

            _db.ContactFormPluginLogs.Add(new ContactFormPluginLog
            {
                TenantId = tenant.Id,
                AgentId = agentId.Value,
                SubjectId = notification.SubjectId,
                SubjectIdSource = SubjectIdSource,
                Action = ActionContactFormSubmit
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
