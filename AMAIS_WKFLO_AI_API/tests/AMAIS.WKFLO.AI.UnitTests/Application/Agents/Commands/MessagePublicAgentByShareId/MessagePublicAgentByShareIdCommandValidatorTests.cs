using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.MessagePublicAgentByShareId
{
    /// <summary>
    /// IsContactFormReplay is client-supplied and must never be trusted on its own — Copilot PR review
    /// round 2 flagged this as a moderate security/audit-integrity gap (any caller could mark an
    /// arbitrary message as a "replay" to have it silently excluded from Chat Logs/analytics). These tests
    /// prove the server-side pending-replay check (IContactFormReplayStateService) actually gates the flag.
    /// </summary>
    public class MessagePublicAgentByShareIdCommandValidatorTests
    {
        private const string TenantName = "test-tenant";
        private const string ShareId = "share-abc";
        private const string SessionId = "session-1";
        private const string OriginalQuestion = "what are your hours?";

        private readonly Mock<IContactFormReplayStateService> _replayStateMock;
        private readonly MessagePublicAgentByShareIdCommandValidator _validator;

        public MessagePublicAgentByShareIdCommandValidatorTests()
        {
            var tenant = new Tenant { Id = 1, Name = TenantName };
            var agent = new TenantAgent { Id = 5, Tenant = tenant, IsActive = true };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Tenants).Returns(new List<Tenant> { tenant }.BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>
            {
                new() { ShareId = ShareId, Agent = agent }
            }.BuildMockDbSet().Object);

            _replayStateMock = new Mock<IContactFormReplayStateService>();

            _validator = new MessagePublicAgentByShareIdCommandValidator(db.Object, new TenantContext { TenantName = TenantName }, _replayStateMock.Object);
        }

        private static MessagePublicAgentByShareIdCommand BuildCommand(bool isReplay, string text = OriginalQuestion) => new()
        {
            ShareId = ShareId,
            SessionId = SessionId,
            Input = new MessagePublicAgentInputCommand { Text = text },
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
            // No SubmitPublicAgentContactFormCommandHandler ever ran for this session — either a genuine
            // client bug or an abuse attempt to suppress topic classification/chat-history persistence for
            // an arbitrary message. Must be rejected clearly, not silently fall through as a normal message.
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactFormReplayPendingState)null);

            var command = BuildCommand(isReplay: true);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(MessagePublicAgentByShareIdCommand.IsContactFormReplay));
        }

        [Fact]
        public async Task Validate_ReplayWithMismatchedText_IsRejected()
        {
            // Defense in depth: even with a pending replay, the replayed text must match what was
            // originally deferred.
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
            // MessagePublicAgentByShareIdCommandHandler), a second IsContactFormReplay=true request
            // without a fresh contact-form submission must find nothing pending and be rejected.
            _replayStateMock
                .Setup(x => x.GetPendingReplayAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactFormReplayPendingState)null);

            var command = BuildCommand(isReplay: true);

            var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
        }
    }
}
