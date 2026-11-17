using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.SearchRunnableAgentVectorDataByEmail
{
    public class SearchRunnableAgentVectorDataByEmailQuery : IWkfloRequest<IEnumerable<string>>
    {
        [JsonIgnore]
        public RunnableAgent Agent { get; set; }

        public string Message { get; set; }
    }
}

