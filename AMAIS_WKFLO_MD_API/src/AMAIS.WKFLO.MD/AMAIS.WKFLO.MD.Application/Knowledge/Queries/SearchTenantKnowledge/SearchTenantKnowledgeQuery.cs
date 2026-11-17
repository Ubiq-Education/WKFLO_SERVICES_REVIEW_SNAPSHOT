using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Queries.SearchTenantKnowledge
{
    public class SearchTenantKnowledgeQuery : IWkfloRequest<Result>
    {
        public string Query { get; set; }
        public List<string> Tags { get; set; }
        public int Limit { get; set; }
    }
}

