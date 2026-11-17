using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.WorkingState;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatSessionState
{
    /// <summary>
    /// US 7506: reads the session's shared working state. Work requested before the session's last Stop does not count
    /// (it is being cancelled). Scoped by the request tenant, like the Stop. When the state cannot be read the session is
    /// reported idle: the live SignalR events still show any work once the chat is joined again.
    /// </summary>
    public class GetPublicAgentChatSessionStateQueryHandler : IRequestHandler<GetPublicAgentChatSessionStateQuery, Result>
    {
        private readonly ISessionWorkingStateStore _workingState;
        private readonly IChatStopStateStore _stopState;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<GetPublicAgentChatSessionStateQueryHandler> _logger;

        public GetPublicAgentChatSessionStateQueryHandler(ISessionWorkingStateStore workingState, IChatStopStateStore stopState, TenantContext tenantContext,
            ILogger<GetPublicAgentChatSessionStateQueryHandler> logger)
        {
            _workingState = workingState;
            _stopState = stopState;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(GetPublicAgentChatSessionStateQuery query, CancellationToken cancellationToken)
        {
            var isWorking = false;

            try
            {
                var stoppedAt = await _stopState.GetStoppedAtAsync(_tenantContext.TenantName, query.SessionId, cancellationToken);
                isWorking = await _workingState.IsWorkingAsync(_tenantContext.TenantName, query.SessionId, stoppedAt, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reading the working state of session {SessionId} failed; reporting it idle", query.SessionId);
            }

            return new SuccessResult<PublicAgentChatSessionStateResult>(new PublicAgentChatSessionStateResult { IsWorking = isWorking });
        }
    }
}
