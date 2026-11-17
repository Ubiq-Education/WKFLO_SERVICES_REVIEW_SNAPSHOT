using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Commands.ScheduleClassificationJob
{
    public class ScheduleClassificationJobCommand : IWkfloRequest<Result>
    {
        public int AgentId { get; set; }
    }
}

