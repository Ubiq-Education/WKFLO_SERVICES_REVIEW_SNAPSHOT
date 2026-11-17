using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Dapr.Workflow;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// WI 7575: an unanswered tool approval request expires (= denied) after <see cref="ToolApprovalConsts.Expiry"/>.
    /// A durable Dapr timer, so it survives restarts and works across instances. Registered under
    /// <see cref="DaprToolApprovalExpiryScheduler.WorkflowName"/>.
    /// </summary>
    public class ToolApprovalExpiryWorkflow : Workflow<ToolApprovalExpiryWorkflowInput, bool>
    {
        public override async Task<bool> RunAsync(WorkflowContext context, ToolApprovalExpiryWorkflowInput input)
        {
            await context.CreateTimer(ToolApprovalConsts.Expiry);
            return await context.CallActivityAsync<bool>(nameof(ToolApprovalExpiryWorkflowActivity), input);
        }
    }

    public class ToolApprovalExpiryWorkflowActivity : WorkflowActivity<ToolApprovalExpiryWorkflowInput, bool>
    {
        private readonly IToolApprovalStateService _stateService;
        private readonly IToolApprovalNotifier _notifier;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILogger<ToolApprovalExpiryWorkflowActivity> _logger;

        public ToolApprovalExpiryWorkflowActivity(IToolApprovalStateService stateService, IToolApprovalNotifier notifier,
            IDateTimeProvider dateTimeProvider, ILogger<ToolApprovalExpiryWorkflowActivity> logger)
        {
            _stateService = stateService;
            _notifier = notifier;
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        public override async Task<bool> RunAsync(WorkflowActivityContext context, ToolApprovalExpiryWorkflowInput input)
        {
            try
            {
                // Null when it was decided in time (or another path expired it already): nothing to do.
                var expired = await _stateService.TryExpireAsync(input.ApprovalId, _dateTimeProvider.DateTimeOffsetUtcNow());
                if (expired == null)
                    return true;

                await _notifier.NotifyResolvedAsync(expired);
                await _notifier.PersistExpiredAsync(expired);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expiring tool approval {ApprovalId} failed", input?.ApprovalId);
                return false;
            }
        }
    }
}
