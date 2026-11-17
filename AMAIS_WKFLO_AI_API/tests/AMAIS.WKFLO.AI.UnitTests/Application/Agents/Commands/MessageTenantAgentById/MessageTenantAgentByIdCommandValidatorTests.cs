using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.MessageTenantAgentById
{
    /// <summary>
    /// IsContactFormReplay is client-supplied and must never be trusted on its own — Copilot PR review
    /// round 2 flagged this as a moderate security/audit-integrity gap on the authenticated tenant surface
    /// too (any authenticated caller could mark an arbitrary message as a "replay" to have it silently
    /// excluded from Chat Logs/analytics). These tests mirror
    /// MessagePublicAgentByShareIdCommandValidatorTests for this tenant-side command.
    /// </summary>
    public class MessageTenantAgentByIdCommandValidatorTests
    {
        private const string TenantName = "test-tenant";
        private const int AgentId = 5;
        private const string SessionId = "session-1";
        private const string OriginalQuestion = "what are your hours?";

        private readonly Mock<IContactFormReplayStateService> _replayStateMock;
        private readonly MessageTenantAgentByIdCommandValidator _validator;

        public MessageTenantAgentByIdCommandValidatorTests()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new TenantAgent { Id = AgentId, Tenant = tenant, IsActive = true };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { tenant }.BuildMockDbSet().Object);
            db.Setup(x => x.TenantAgents).Returns(new List<TenantAgent> { agent }.BuildMockDbSet().Object);

            _replayStateMock = new Mock<IContactFormReplayStateService>();

            _validator = new MessageTenantAgentByIdCommandValidator(db.Object, new TenantContext { TenantName = TenantName }, _replayStateMock.Object);
        }

        private static MessageTenantAgentByIdCommand BuildCommand(bool isReplay, string text = OriginalQuestion) => new()
        {
            Id = AgentId,
            SessionId = SessionId,
            Input = new MessageTenantAgentInputCommand { Text = text },
            IsContactFormReplay = isReplay
        };

        [Fact]
        public async Task Validate_NonReplayMessage_DoesNotCheckPendingState()
        {
            var command = BuildCommand(isReplay: false);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            _replayStateMock.Verify(x => x.GetPendingReplayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Validate_ReplayWithMatchingPendingState_IsHonored()
        {
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ContactFormReplayPendingState { OriginalMessage = OriginalQuestion });

            var command = BuildCommand(isReplay: true);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
        }

        [Fact]
        public async Task Validate_ReplayWithNoPendingState_IsRejected()
        {
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactFormReplayPendingState)null);

            var command = BuildCommand(isReplay: true);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MessageTenantAgentByIdCommand.IsContactFormReplay));
        }

        [Fact]
        public async Task Validate_ReplayWithMismatchedText_IsRejected()
        {
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ContactFormReplayPendingState { OriginalMessage = "a completely different question" });

            var command = BuildCommand(isReplay: true, text: OriginalQuestion);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task Validate_SecondReplayAfterStateAlreadyConsumed_IsRejected()
        {
            // Single-use guard: once ConsumePendingReplayAsync has run for a session (see
            // MessageTenantAgentByIdCommandHandler), a second IsContactFormReplay=true request without a
            // fresh contact-form submission must find nothing pending and be rejected.
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactFormReplayPendingState)null);

            var command = BuildCommand(isReplay: true);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
        }
    }
}
