using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentToolPermissions
{
    /// <summary>WI 7575: every tool linked to a tenant agent with the agent's permission for it.</summary>
    public class GetTenantAgentToolPermissionsQuery : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }
    }
}
