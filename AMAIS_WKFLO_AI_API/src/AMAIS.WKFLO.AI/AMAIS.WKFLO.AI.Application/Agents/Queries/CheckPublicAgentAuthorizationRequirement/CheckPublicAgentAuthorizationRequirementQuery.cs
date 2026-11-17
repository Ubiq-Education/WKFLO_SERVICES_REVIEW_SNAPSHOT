using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentAuthorizationRequirement
{
    public class CheckPublicAgentAuthorizationRequirementQuery : IWkfloRequest<Result>, IRequestShareId<string>
    {
        public string ShareId { get; set; }
    }
}
