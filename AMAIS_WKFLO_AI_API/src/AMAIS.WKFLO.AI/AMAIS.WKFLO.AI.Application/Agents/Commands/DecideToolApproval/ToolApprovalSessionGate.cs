using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval
{
    /// <summary>
    /// WI 7575: when the person sends a new message instead of answering, every approval still open in the session
    /// expires (treated as denied) and the chat is told. The new turn then brings the persisted cards up to date.
    /// </summary>
    public class ToolApprovalSessionGate
    {
        private readonly IToolApprovalStateService _stateService;
        private readonly IToolApprovalNotifier _notifier;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<ToolApprovalSessionGate> _logger;

        public ToolApprovalSessionGate(IToolApprovalStateService stateService, IToolApprovalNotifier notifier, IDateTimeProvider dateTimeProvider, ILogger<ToolApprovalSessionGate> logger)
        {
            _stateService = stateService;
            _notifier = notifier;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <summary>US 7501: the Stop - only the open approvals of the caller tenant are expired.</summary>
        /// <returns>
        /// How many approvals the state store expired (0 when the expiry itself failed). A failed notification does not
        /// change it: the approval is expired either way.
        /// </returns>
        public async Task<int> ExpireOpenApprovalsAsync(string tenantName, string sessionId, CancellationToken cancellationToken)
        {
            List<ToolApprovalRecord> expired;
            try
            {
                expired = (await _stateService.ExpireOpenForSessionAsync(tenantName, sessionId, _dateTimeProvider.DateTimeOffsetUtcNow(), cancellationToken)).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Expiring open tool approvals of session {SessionId} failed", sessionId);
                return 0;
            }

            try
            {
                foreach (var record in expired)
                {
                    await _notifier.NotifyResolvedAsync(record);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Notifying the expired tool approvals of session {SessionId} failed", sessionId);
            }

            return expired.Count;
        }

        public async Task ExpireOpenApprovalsAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var expired in await _stateService.ExpireOpenForSessionAsync(sessionId, _dateTimeProvider.DateTimeOffsetUtcNow(), cancellationToken))
                {
                    await _notifier.NotifyResolvedAsync(expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never block the user's message on this; the stored records still expire on their own timer.
                _logger.LogWarning(ex, "Expiring open tool approvals of session {SessionId} failed", sessionId);
            }
        }
    }
}
