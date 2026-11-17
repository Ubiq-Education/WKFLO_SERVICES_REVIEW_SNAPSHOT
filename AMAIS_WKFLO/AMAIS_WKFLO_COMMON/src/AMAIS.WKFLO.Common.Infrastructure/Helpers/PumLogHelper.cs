using AMAIS.PUM.Log;
using AMAIS.PUM.Models;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.Infrastructure.Helpers
{
    public static class PumLogHelper
    {
        const string ServiceName = "Agent";
        const string ServiceType = "AIServices";

        public static async Task<UsageLogClient> ReportUsageAsync(this UsageLogClient client, string tenantName, string sessionId, string userName, string userGuid,
            AgentAnswer agentAnswer, decimal? credits, IDateTimeProvider dateTimeProvider)
        {
            if (client != null)
            {
                await PumLogHelper.ReportUsageAsync(client, tenantName, sessionId, userName, userGuid, agentAnswer.TokenUsage, agentAnswer.AgentType,
                    agentAnswer.AgentTags, agentAnswer.AgentId, agentAnswer.AIModelId, credits, dateTimeProvider);
            }

            return client;
        }

        // WI 7365: AgentTags/AgentId are optional — not every AgentAnswer is built from a full Agent
        // entity (e.g. ImageGenerationPlugin only carries a pre-joined agentType string), and AgentTags
        // itself can be null/empty when the agent has no tags assigned. Both are defaulted below instead
        // of allowed to throw, matching this helper's existing defensive style.
        // WI 7303: aiModelId/credits are likewise optional — see AgentAnswer.AIModelId and
        // PumUsageReporter.ComputeCreditsAsync for when they're null.
        private static async Task ReportUsageAsync(UsageLogClient client, string tenantName, string sessionId, string userName, string userGuid,
            AITokenUsage tokenUsage, string agentType, List<string> agentTags, int? agentId, int? aiModelId, decimal? credits, IDateTimeProvider dateTimeProvider)
        {
            var dataSample = BuildDataSample(tenantName, sessionId, userName, userGuid, tokenUsage, agentType, agentTags, agentId, aiModelId, credits, dateTimeProvider);

            await client.SendLogAsync(dataSample);
        }

        // Extracted from the private ReportUsageAsync overload above so the resulting Metadata can be
        // asserted directly in unit tests without going through UsageLogClient.SendLogAsync, whose members
        // aren't virtual/mockable and which performs a real Event Hub send.
        internal static UsageLogDataSample BuildDataSample(string tenantName, string sessionId, string userName, string userGuid,
            AITokenUsage tokenUsage, string agentType, List<string> agentTags, int? agentId, int? aiModelId, decimal? credits, IDateTimeProvider dateTimeProvider)
        {
            // WI 7365 follow-up: a tenant can tag a wkflo_services agent "Chatbot" to have it stand in for
            // the site chatbot, in which case its usage counts toward the Chatbot pool instead of the
            // default MAISY-and-sub-agents one.
            bool isChatbotTagged = agentTags?.Contains(CommonConsts.PumPoolConsts.Chatbot, StringComparer.OrdinalIgnoreCase) == true;
            string pool = isChatbotTagged ? CommonConsts.PumPoolConsts.Chatbot : CommonConsts.PumPoolConsts.MaisyAndSubagents;

            return new UsageLogDataSample()
            {
                Id = Guid.NewGuid(),
                SourceName = agentType + ServiceName,
                SourceType = ServiceType,
                TenantName = tenantName,
                Time = dateTimeProvider.DateTimeUtcNow(),
                Value = (int)tokenUsage.TotalTokenCount,
                Metadata =
                {
                    { "InputTokenCount", tokenUsage.InputTokenCount.ToString() },
                    { "OutputTokenCount", tokenUsage.OutputTokenCount.ToString() },
                    { "SessionId", sessionId },
                    { "UserName", userName },
                    { "UserGuid", userGuid },
                    { "AgentTags", agentTags ?? new List<string>() },
                    { "AgentId", agentId?.ToString() },
                    { "AIModelId", aiModelId?.ToString() },
                    { "Credits", credits },
                    { "Pool", pool }
                }
            };
        }
    }
}
