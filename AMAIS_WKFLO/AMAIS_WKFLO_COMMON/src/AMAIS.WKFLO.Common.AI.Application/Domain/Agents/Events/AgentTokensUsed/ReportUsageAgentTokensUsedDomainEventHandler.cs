using AMAIS.WKFLO.Common.Interfaces.Services;
using Mediator;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.AgentTokensUsed
{
    public class ReportUsageAgentTokensUsedDomainEventHandler : INotificationHandler<AgentTokensUsedDomainEvent>
    {
        private readonly IPumUsageReporter _pumReporter;

        public ReportUsageAgentTokensUsedDomainEventHandler(IPumUsageReporter pumReporter)
        {
            _pumReporter = pumReporter;
        }

        public async ValueTask Handle(AgentTokensUsedDomainEvent notification, CancellationToken cancellationToken)
        {
            await _pumReporter.ReportAsync(notification.AgentAnswer);
        }
    }
}
