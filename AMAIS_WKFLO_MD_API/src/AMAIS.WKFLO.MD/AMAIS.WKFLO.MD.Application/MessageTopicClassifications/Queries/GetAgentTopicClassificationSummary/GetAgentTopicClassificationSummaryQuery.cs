using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Models;

namespace AMAIS.WKFLO.MD.Application.MessageTopicClassifications.Queries.GetAgentTopicClassificationSummary
{
    public class GetAgentTopicClassificationSummaryQuery : IWkfloRequest<Result>
    {
        public int AgentId { get; set; }
        public DateRangeOption DateRange { get; set; } = DateRangeOption.Last30Days;
    }
}

