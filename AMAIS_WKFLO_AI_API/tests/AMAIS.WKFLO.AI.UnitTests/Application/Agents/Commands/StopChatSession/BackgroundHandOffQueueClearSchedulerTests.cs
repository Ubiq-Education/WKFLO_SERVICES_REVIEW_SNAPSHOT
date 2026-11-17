using AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.StopChatSession
{
    /// <summary>US 7501: the Stop response never waits for the hand-off queues to be cleared.</summary>
    public class BackgroundHandOffQueueClearSchedulerTests
    {
        private static readonly DateTimeOffset StoppedAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        private readonly Mock<ISessionHandOffQueueClearer> _clearer = new();
        private readonly TenantContext _requestTenant = new() { TenantName = "tenant-a", TenantGuid = "tenant-guid" };
        private TenantContext _seenTenant;
        private CancellationToken _seenToken;
        private IServiceProvider _scopeServices;

        private BackgroundHandOffQueueClearScheduler BuildScheduler()
        {
            var services = new ServiceCollection();
            services.AddScoped<TenantContext>();
            services.AddSingleton(WkfloMapperExtensions.CreateMapper());
            services.AddScoped(sp =>
            {
                _scopeServices = sp;
                return _clearer.Object;
            });
            var provider = services.BuildServiceProvider();

            return new BackgroundHandOffQueueClearScheduler(provider.GetRequiredService<IServiceScopeFactory>(), WkfloMapperExtensions.CreateMapper(),
                _requestTenant, NullLogger<BackgroundHandOffQueueClearScheduler>.Instance);
        }

        [Fact]
        public async Task Schedule_ClearsInItsOwnScopeWithTheRequestTenant_AndCannotBeCancelledByTheRequest()
        {
            var scheduler = BuildScheduler();
            _clearer.Setup(x => x.ClearAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Callback<string, int?, string, DateTimeOffset, CancellationToken>((_, _, _, _, ct) =>
                {
                    _seenTenant = _scopeServices.GetRequiredService<TenantContext>();
                    _seenToken = ct;
                })
                .Returns(Task.CompletedTask);

            scheduler.Schedule("session-1", 5, null, StoppedAt);

            // The request scope ends: changing its tenant must not affect the running clear.
            _requestTenant.TenantName = "changed";
            await scheduler.LastClear;

            _clearer.Verify(x => x.ClearAsync("session-1", 5, null, StoppedAt, It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotSame(_requestTenant, _seenTenant);
            Assert.Equal("tenant-a", _seenTenant.TenantName);
            Assert.Equal("tenant-guid", _seenTenant.TenantGuid);
            Assert.False(_seenToken.CanBeCanceled);
        }

        [Fact]
        public async Task Schedule_ClearFails_IsLoggedNotThrown()
        {
            var scheduler = BuildScheduler();
            _clearer.Setup(x => x.ClearAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("db down"));

            scheduler.Schedule("session-1", null, "share-1", StoppedAt);

            await scheduler.LastClear; // completes without throwing
            Assert.True(scheduler.LastClear.IsCompletedSuccessfully);
        }
    }
}
