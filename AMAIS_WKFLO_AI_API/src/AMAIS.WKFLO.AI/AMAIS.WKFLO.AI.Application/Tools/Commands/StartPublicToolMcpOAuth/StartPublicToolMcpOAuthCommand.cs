using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Tools.Commands.StartPublicToolMcpOAuth
{
    /// <summary>
    /// The public chat widget's equivalent of MD API's tenant <c>StartToolMcpOAuthCommand</c> — begins the
    /// interactive OAuth 2.1 consent flow for an OAuth2-authenticated MCP tool attached to a publicly shared
    /// agent, reached from the chat widget itself (no tenant-admin session). See
    /// <c>StartPublicToolMcpOAuthCommandHandler</c> for the additional shareId/tool-ownership check this
    /// path needs that the tenant-scoped path doesn't.
    /// </summary>
    public class StartPublicToolMcpOAuthCommand : IWkfloRequest<Result>, IRequestShareId<string>, IRequestId<Guid>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }

        /// <summary>The tool's <c>RunableTool.McpOAuthConnectionId</c>.</summary>
        [Required(ErrorMessage = ErrorConst.Required)]
        public Guid Id { get; set; }
    }
}
