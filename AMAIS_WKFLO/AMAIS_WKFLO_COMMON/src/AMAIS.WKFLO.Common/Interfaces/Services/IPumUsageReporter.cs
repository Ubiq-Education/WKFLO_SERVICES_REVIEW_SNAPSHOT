using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    public interface IPumUsageReporter
    {
        Task ReportAsync(AgentAnswer agentAnswer);
    }
}
