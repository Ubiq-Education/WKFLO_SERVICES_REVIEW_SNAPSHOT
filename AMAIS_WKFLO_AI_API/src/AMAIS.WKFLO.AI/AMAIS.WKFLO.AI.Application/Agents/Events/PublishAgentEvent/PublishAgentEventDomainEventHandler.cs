using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.Mediator;
using Mediator;

namespace AMAIS.WKFLO.AI.Application.Agents.Events.PublishAgentEvent
{
    /// <summary>
    /// AI.Application-side half of the bridge for <see cref="PublishAgentEventDomainEvent"/> (story
    /// #6480): <c>Common.AI.Application</c>'s <c>AgentEventPublisherPlugin</c> publishes this event
    /// (once per target agent) because it cannot reference this project's
    /// <see cref="PublishAgentEventCommand"/> directly. This handler does the one thing Common
    /// couldn't — construct the real command and send it through the existing, already-validated
    /// publish pipeline (topic, <c>WkfloQueueMessage</c> wrapping,
    /// <see cref="PublishAgentEventCommandValidator"/>) exactly as if it had arrived via
    /// <c>AgentEventPublish</c>'s HTTP endpoint.
    /// </summary>
    public class PublishAgentEventDomainEventHandler : INotificationHandler<PublishAgentEventDomainEvent>
    {
        private readonly IWkfloMediator _mediator;

        public PublishAgentEventDomainEventHandler(IWkfloMediator mediator)
        {
            _mediator = mediator;
        }

        public async ValueTask Handle(PublishAgentEventDomainEvent notification, CancellationToken cancellationToken)
        {
            var command = new PublishAgentEventCommand
            {
                Id = notification.AgentId,
                AgentRole = notification.AgentRole,
                Input = new AgentInvocationInput { Text = notification.Text, Files = notification.Files },
                SessionId = notification.SessionId,
                EventType = notification.EventType,
                RootIsWidget = notification.RootIsWidget,
                ApprovalRootAgentId = notification.ApprovalRootAgentId,
                RequestedByTurnStartedAt = notification.RequestedByTurnStartedAt
            };

            await _mediator.Send(command, cancellationToken);
        }
    }
}
