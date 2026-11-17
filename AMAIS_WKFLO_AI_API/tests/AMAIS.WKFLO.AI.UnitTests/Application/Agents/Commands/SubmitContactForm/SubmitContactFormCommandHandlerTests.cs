using AMAIS.WKFLO.AI.Application.Agents.Commands.SubmitContactForm;
using AMAIS.WKFLO.AI.Application.Agents.Events.ContactFormSubmitted;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.SubmitContactForm
{
    public class SubmitContactFormCommandHandlerTests
    {
        private readonly Mock<IWkfloMediator> _mediatorMock;
        private readonly Mock<IChatNotificationService> _notificationMock;
        private readonly Mock<IContactFormReplayStateService> _replayStateMock;
        private readonly SubmitContactFormCommandHandler _handler;

        public SubmitContactFormCommandHandlerTests()
        {
            _mediatorMock = new Mock<IWkfloMediator>();
            _notificationMock = new Mock<IChatNotificationService>();
            _replayStateMock = new Mock<IContactFormReplayStateService>();
            var tenantContext = new TenantContext { TenantName = "test-tenant" };

            _mediatorMock
                .Setup(x => x.Publish(It.IsAny<ContactFormSubmittedDomainEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _notificationMock
                .Setup(x => x.SendContactFormCompletedAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            _replayStateMock
                .Setup(x => x.SetPendingReplayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _handler = new SubmitContactFormCommandHandler(_mediatorMock.Object, tenantContext, _notificationMock.Object, _replayStateMock.Object);
        }

        [Theory]
        [InlineData("subject-001", "session-001", "John", "Doe", "john@example.com")]
        [InlineData("subject-002", "session-002", "", "", "")]
        public async Task Handle_ValidCommand_ReturnsSuccessResult(
            string subjectId, string sessionId, string firstName, string lastName, string email)
        {
            var command = new SubmitContactFormCommand
            {
                SubjectId = subjectId,
                SessionId = sessionId,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            };

            // Act
            var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<bool>>(result);
            Assert.Equal(ResultType.Ok, result.ResultType);
        }

        [Fact]
        public async Task Handle_PublishesContactFormSubmittedEvent()
        {
            var command = new SubmitContactFormCommand
            {
                SubjectId = "sub-xyz",
                SessionId = "sess-xyz",
                FirstName = "Jane",
                LastName = "Smith",
                Email = "jane@example.com"
            };

            // Act
            await _handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            _mediatorMock.Verify(x => x.Publish(
                It.Is<ContactFormSubmittedDomainEvent>(e =>
                    e.TenantName == "test-tenant" &&
                    e.SubjectId == "sub-xyz" &&
                    e.SessionId == "sess-xyz"),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_WithOriginalMessage_RecordsPendingReplayBeforeNotifying()
        {
            // Server-side counterpart to wkflo_ui's ContactFormCompleted replay: this is the ONLY point
            // that may legitimately mark a session as having a pending replay — see
            // IContactFormReplayStateService for why MessagePublicAgentByShareIdCommand/
            // MessageTenantAgentByIdCommand's IsContactFormReplay flag must never be trusted without it.
            var command = new SubmitContactFormCommand
            {
                SubjectId = "sub-abc",
                SessionId = "sess-abc",
                OriginalMessage = "What are your opening hours?"
            };

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            _replayStateMock.Verify(x => x.SetPendingReplayAsync("sess-abc", "What are your opening hours?", It.IsAny<CancellationToken>()), Times.Once);
            _notificationMock.Verify(x => x.SendContactFormCompletedAsync("sess-abc", "What are your opening hours?"), Times.Once);
        }

        [Fact]
        public async Task Handle_WithoutOriginalMessage_DoesNotRecordPendingReplay()
        {
            var command = new SubmitContactFormCommand
            {
                SubjectId = "sub-def",
                SessionId = "sess-def",
                OriginalMessage = string.Empty
            };

            await _handler.Handle(command, TestContext.Current.CancellationToken);

            _replayStateMock.Verify(x => x.SetPendingReplayAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
