using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.ClassificationJobs.Queries.GetClassificationJobStatus
{
    public class GetClassificationJobStatusQuery : IWkfloRequest<Result>
    {
        public int AgentId { get; set; }
    }
}

