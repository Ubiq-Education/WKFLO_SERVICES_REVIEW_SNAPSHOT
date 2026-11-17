using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.UpdateTenantKnowledgeDescription
{
    public class UpdateTenantKnowledgeDescriptionCommand : IWkfloRequest<Result>
    {
        public string DocId { get; set; }
        public string Description { get; set; }
    }
}
