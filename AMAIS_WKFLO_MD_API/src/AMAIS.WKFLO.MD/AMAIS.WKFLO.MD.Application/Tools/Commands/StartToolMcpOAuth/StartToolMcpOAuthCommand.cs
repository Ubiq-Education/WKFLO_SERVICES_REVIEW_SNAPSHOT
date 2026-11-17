using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.StartToolMcpOAuth
{
    /// <summary>
    /// Begins the interactive OAuth 2.1 consent flow for an OAuth2-authenticated MCP tool and returns the
    /// authorization URL the client (wkflo_ui) should navigate/redirect the browser to next. Deliberately
    /// returns a normal authenticated JSON result rather than issuing an HTTP redirect itself, because
    /// there is no way for a plain browser navigation to carry the Bearer token / tenant header every other
    /// tenant endpoint requires — only the "callback" endpoint (hit directly by the authorization server's
    /// redirect) is unauthenticated.
    /// </summary>
    public class StartToolMcpOAuthCommand : IWkfloRequest<Result>, IRequestId<Guid>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public Guid Id { get; set; }
    }
}
