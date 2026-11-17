using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentConversationBySessionId
{
    public class GetTenantAgentConversationBySessionIdQuery : IWkfloRequest<Result>
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
    }
}

