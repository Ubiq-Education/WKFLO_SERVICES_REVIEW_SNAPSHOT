using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeTags
{
    public class UpdateTenantKnowledgeTagsCommand : IWkfloRequest<Result>
    {
        public string DocId { get; set; }
        public List<string> Tags { get; set; }
    }
}

