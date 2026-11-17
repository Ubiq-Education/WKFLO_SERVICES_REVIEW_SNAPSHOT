using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Tools.Commands.DiscoverTenantToolFunctions
{
    /// <summary>
    /// WI 7447: connects to a tenant MCP tool's server with its own config/auth (for a per-user OAuth tool:
    /// the calling admin's own connection), runs <c>tools/list</c> and stores the result as the tool's function
    /// snapshot. Returns the same shape as MD API's GET <c>tenant/tool/{id}/functions</c>.
    /// </summary>
    public class DiscoverTenantToolFunctionsCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }
    }
}
