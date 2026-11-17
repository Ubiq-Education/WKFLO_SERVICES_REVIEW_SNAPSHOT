using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth
{
    /// <summary>
    /// Handles the authorization server's redirect back to us after the tenant grants (or denies) consent.
    /// Unlike every other tenant Tool command, this one is reached by a bare browser navigation the
    /// authorization server controls — it cannot carry our Bearer token or tenant header, and (since the
    /// callback route is now a single static URL shared by every connection — see
    /// <see cref="AMAIS.WKFLO.MD.Application.Tools.Services.McpOAuthCallbackUrlBuilder"/>) no connection id
    /// either — so it is deliberately NOT tenant-scoped and carries no <c>Id</c>. <see cref="State"/> (the
    /// one-time authorization <see cref="Code"/>'s CSRF-protection counterpart, validated end-to-end,
    /// including PKCE, by <c>McpOAuthConsentService</c>'s own hand-rolled OAuth 2.1 implementation) is what
    /// correlates this callback back to the right pending consent flow — see
    /// <c>McpOAuthConsentService.CompleteAsync</c>.
    /// </summary>
    /// <remarks><see cref="IOAuthCallbackRequest"/>: <see cref="Code"/> and <see cref="State"/> are masked in request logs.</remarks>
    public class CompleteToolMcpOAuthCommand : IWkfloRequest<Result>, IOAuthCallbackRequest
    {
        /// <summary>The authorization code. Null/empty when the authorization server reports an error instead (e.g. the tenant denied consent).</summary>
        public string Code { get; set; }

        public string State { get; set; }

        public string Iss { get; set; }
    }
}
