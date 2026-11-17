using AMAIS.PUM.Log;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Services.PUM
{
    public class PumUsageReporter : IPumUsageReporter
    {
        private readonly UsageLogClient _logClient;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly UserContext _userContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly IMasterDataDbContext _db;
        private readonly ILogger<PumUsageReporter> _logger;

        public PumUsageReporter(UsageLogClient logClient, TenantContext tenantContext, ChatContext chatContext, UserContext userContext, IDateTimeProvider dateTimeProvider, IMasterDataDbContext db, ILogger<PumUsageReporter> logger)
        {
            _logClient = logClient;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _userContext = userContext;
            _dateTimeProvider = dateTimeProvider;
            _db = db;
            _logger = logger;
        }

        public async Task ReportAsync(AgentAnswer agentAnswer)
        {
            decimal? credits = await ComputeCreditsAsync(agentAnswer);

            await _logClient.ReportUsageAsync(_tenantContext?.TenantName, _chatContext?.SessionId,
                    ResolveReportedUserName(_userContext),
                    _userContext?.Guid, agentAnswer, credits, _dateTimeProvider);
        }

        // Extracted purely for testability: ReportAsync ultimately calls UsageLogClient.ReportUsageAsync,
        // whose members aren't virtual/mockable, so this identity-selection step can't be verified by
        // exercising ReportAsync itself - same reasoning as ComputeCreditsAsync below being internal
        // rather than inlined.
        internal static string ResolveReportedUserName(UserContext userContext) => userContext?.Email;

        // WI 7303: real per-call credits = (actualTokensIn * MultiplierIn / 1000) + (actualTokensOut * MultiplierOut / 1000),
        // computed from the actual TokenUsage of this call against the AICredit configured for the AIModel actually
        // used - NOT from AICredit's own reference TokensIn/TokensOut example values (those back the unrelated
        // SQL-computed AICredit.Credits column). Returns null (never 0) when AIModelId is unknown or the model has
        // no AICredit configured yet, since 0 would misleadingly claim "this usage cost zero credits".
        //
        // This runs on the agent-response critical path (published before chat history is persisted/returned to
        // the caller - see AgentTokensUsedDomainEvent's call sites), so a transient failure of this DB lookup must
        // never fail the whole chat turn just to report usage. Swallow and log instead, same "never crash, never
        // claim 0" intent as the AIModelId/AICredit-missing cases above.
        internal async Task<decimal?> ComputeCreditsAsync(AgentAnswer agentAnswer)
        {
            if (!agentAnswer.AIModelId.HasValue)
                return null;

            try
            {
                var aiCredit = await _db.AICredits.AsNoTracking()
                    .Where(c => c.AIModelId == agentAnswer.AIModelId.Value && !c.IsDeleted)
                    .FirstOrDefaultAsync();

                if (aiCredit == null)
                    return null;

                return (agentAnswer.TokenUsage.InputTokenCount * aiCredit.MultiplierIn / 1000m)
                     + (agentAnswer.TokenUsage.OutputTokenCount * aiCredit.MultiplierOut / 1000m);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compute PUM credits for AIModelId {AIModelId} - reporting without credits", agentAnswer.AIModelId);
                return null;
            }
        }
    }

    public sealed class NullPumUsageReporter : IPumUsageReporter
    {
        public Task ReportAsync(AgentAnswer agentAnswer) => Task.CompletedTask;
    }
}
