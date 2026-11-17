using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StopChatSession
{
    /// <summary>
    /// US 7501: starts dropping the session's queued hand-offs (<see cref="ISessionHandOffQueueClearer"/>) without
    /// making the Stop request wait for it.
    /// </summary>
    public interface IHandOffQueueClearScheduler
    {
        /// <summary>Starts the clear and returns straight away. Failures are logged, never thrown.</summary>
        void Schedule(string sessionId, int? hostAgentId, string hostShareId, DateTimeOffset stoppedAt);
    }

    /// <summary>
    /// Runs the clear in-process, in the background, in its own DI scope with a snapshot of the Stop request's tenant
    /// context (same pattern as <c>BackgroundToolApprovalResumeQueue</c>). An agent actor that is waiting for a
    /// running hand-off only takes the clear call once that (cancelled) run has ended, so awaiting it would hold the
    /// Stop response. Never tied to the request's token: the clear must finish even after the response is sent.
    /// </summary>
    public sealed class BackgroundHandOffQueueClearScheduler : IHandOffQueueClearScheduler
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<BackgroundHandOffQueueClearScheduler> _logger;

        public BackgroundHandOffQueueClearScheduler(IServiceScopeFactory scopeFactory, IWkfloMapper mapper, TenantContext tenantContext,
            ILogger<BackgroundHandOffQueueClearScheduler> logger)
        {
            _scopeFactory = scopeFactory;
            _mapper = mapper;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        /// <summary>The running clear (exposed for tests); production callers do not await it.</summary>
        internal Task LastClear { get; private set; } = Task.CompletedTask;

        public void Schedule(string sessionId, int? hostAgentId, string hostShareId, DateTimeOffset stoppedAt)
        {
            // Snapshot now: the request's scoped contexts are gone once the Stop response is sent.
            var tenant = _mapper.Map<TenantContext>(_tenantContext ?? new TenantContext());

            LastClear = Task.Run(() => RunAsync(tenant, sessionId, hostAgentId, hostShareId, stoppedAt), CancellationToken.None);
        }

        private async Task RunAsync(TenantContext tenant, string sessionId, int? hostAgentId, string hostShareId, DateTimeOffset stoppedAt)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                services.GetRequiredService<IWkfloMapper>().Map(tenant, services.GetRequiredService<TenantContext>());

                await services.GetRequiredService<ISessionHandOffQueueClearer>()
                    .ClearAsync(sessionId, hostAgentId, hostShareId, stoppedAt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Each queued hand-off still checks the stop state before it starts.
                _logger.LogError(ex, "Clearing the queued hand-offs of session {SessionId} failed", sessionId);
            }
        }
    }
}
