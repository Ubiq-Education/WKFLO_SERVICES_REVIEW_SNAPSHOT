using AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent;
using AMAIS.WKFLO.AI.Application.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.PublishAgentEvent;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Events.PublishAgentEvent
{
    public class PublishAgentEventDomainEventHandlerTests
    {
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly PublishAgentEventDomainEventHandler _handler;

        public PublishAgentEventDomainEventHandlerTests()
        {
            _mediatorMock = new Mock<IWkfloMediator>();
            _mediatorMock
                .Setup(x => x.Send(It.IsAny<PublishAgentEventCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuccessResult<object>(null));

            _handler = new PublishAgentEventDomainEventHandler(_mediatorMock.Object);
        }

        [Fact]
        public async Task Handle_NotificationHasFiles_MapsFilesOntoCommandInput()
        {
            var files = new List<FileAttachment>
            {
                new() { FileName = "receipt.png", FileData = "data:image/png;base64,AAA=", FileSize = 3 }
            };
            var notification = new PublishAgentEventDomainEvent(5, "Refunds Bot", "please help", "session-xyz", "refund-requested", files);

            await _handler.Handle(notification, TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Send(
                It.Is<PublishAgentEventCommand>(c => c.Input.Files == files && c.Input.Files.Count == 1),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_ForwardsTheRequestingTurnsStartAndItsStopToken()
        {
            var turnStartedAt = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            using var turn = new CancellationTokenSource();
            var notification = new PublishAgentEventDomainEvent(5, "Kit", "please help", "session-xyz", "escalation", requestedByTurnStartedAt: turnStartedAt);

            await _handler.Handle(notification, turn.Token);

            _mediatorMock.Verify(x => x.Send(It.Is<PublishAgentEventCommand>(c => c.RequestedByTurnStartedAt == turnStartedAt), turn.Token), Times.Once);
        }

        [Fact]
        public async Task Handle_NotificationHasNoFiles_MapsNullFilesOntoCommandInput()
        {
            var notification = new PublishAgentEventDomainEvent(5, "Refunds Bot", "please help", "session-xyz", "refund-requested");

            await _handler.Handle(notification, TestContext.Current.CancellationToken);

            _mediatorMock.Verify(x => x.Send(
                It.Is<PublishAgentEventCommand>(c => c.Input.Files == null),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
