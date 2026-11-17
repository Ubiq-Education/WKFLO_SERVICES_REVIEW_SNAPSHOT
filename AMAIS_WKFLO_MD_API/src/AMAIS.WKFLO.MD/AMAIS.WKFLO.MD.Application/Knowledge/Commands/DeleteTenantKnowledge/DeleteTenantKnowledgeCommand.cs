using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge
{
    public class DeleteTenantKnowledgeCommand : IWkfloRequest<Result>, IRequestId<string>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Id { get; set; }
    }
}

