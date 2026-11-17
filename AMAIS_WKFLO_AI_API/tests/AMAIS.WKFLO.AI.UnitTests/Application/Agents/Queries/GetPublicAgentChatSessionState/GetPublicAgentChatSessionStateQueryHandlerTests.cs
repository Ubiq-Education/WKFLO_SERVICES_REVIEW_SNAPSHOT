using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatSessionState;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.Results;
using Dapr;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetPublicAgentChatSessionState
{
    public class GetPublicAgentChatSessionStateQueryHandlerTests
    {
        private const string TenantName = "tenant-a";
        private const string SessionId = "session-1";
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<ISessionWorkingStateStore> _workingState = new();
        private readonly Mock<IChatStopStateStore> _stopState = new();
        private readonly GetPublicAgentChatSessionStateQueryHandler _handler;

        public GetPublicAgentChatSessionStateQueryHandlerTests()
        {
            _handler = new GetPublicAgentChatSessionStateQueryHandler(_workingState.Object, _stopState.Object, new TenantContext { TenantName = TenantName },
                NullLogger<GetPublicAgentChatSessionStateQueryHandler>.Instance);
        }

        private async Task<bool> IsWorkingAsync()
        {
            var result = await _handler.Handle(new GetPublicAgentChatSessionStateQuery { ShareId = "share", SessionId = SessionId }, TestContext.Current.CancellationToken);
            return Assert.IsType<SuccessResult<PublicAgentChatSessionStateResult>>(result).Data.IsWorking;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Handle_ReportsTheWorkingState_PassingTheLastStop(bool working)
        {
            _stopState.Setup(x => x.GetStoppedAtAsync(TenantName, SessionId, It.IsAny<CancellationToken>())).ReturnsAsync(StoppedAt);
            _workingState.Setup(x => x.IsWorkingAsync(TenantName, SessionId, StoppedAt, It.IsAny<CancellationToken>())).ReturnsAsync(working);

            Assert.Equal(working, await IsWorkingAsync());
        }

        [Fact]
        public async Task Handle_NeverStopped_PassesNoStop()
        {
            _stopState.Setup(x => x.GetStoppedAtAsync(TenantName, SessionId, It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)null);
            _workingState.Setup(x => x.IsWorkingAsync(TenantName, SessionId, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

            Assert.True(await IsWorkingAsync());
        }

        [Fact]
        public async Task Handle_StateUnreadable_ReportsIdle()
        {
            _stopState.Setup(x => x.GetStoppedAtAsync(TenantName, SessionId, It.IsAny<CancellationToken>())).ThrowsAsync(new DaprException("sidecar down"));

            Assert.False(await IsWorkingAsync());
        }
    }
}
