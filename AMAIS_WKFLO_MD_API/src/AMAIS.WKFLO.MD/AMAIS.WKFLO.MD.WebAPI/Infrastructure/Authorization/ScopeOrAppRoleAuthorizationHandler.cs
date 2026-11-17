using Microsoft.AspNetCore.Authorization;

namespace AMAIS.WKFLO.MD.WebAPI.Infrastructure.Authorization
{
    /// <summary>
    /// Authorization handler that validates either delegated permissions (scopes) for user tokens
    /// or application permissions (roles) for machine-to-machine tokens.
    /// </summary>
    public class ScopeOrAppRoleAuthorizationHandler : AuthorizationHandler<ScopeOrAppRoleRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ScopeOrAppRoleRequirement requirement)
        {
            // Check if the token has the required scope (delegated permission - user token)
            var scopeClaim = context.User.FindFirst("http://schemas.microsoft.com/identity/claims/scope")
                ?? context.User.FindFirst("scp");
            if (scopeClaim != null)
            {
                var scopes = scopeClaim.Value.Split(' ');
                if (requirement.AcceptedScopes.Any(scope => scopes.Contains(scope, StringComparer.InvariantCultureIgnoreCase)))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            // Check if the token has the required app role (application permission - M2M token)
            var roleClaims = context.User.FindAll("http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
            if (roleClaims.Any())
            {
                var roles = roleClaims.Select(c => c.Value);
                if (requirement.AcceptedRoles.Any(role => roles.Contains(role, StringComparer.InvariantCultureIgnoreCase)))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            // Also check for roles claim with different claim type (some tokens use this)
            var rolesClaim = context.User.FindFirst("roles");
            if (rolesClaim != null)
            {
                var roles = rolesClaim.Value.Split(' ');
                if (requirement.AcceptedRoles.Any(role => roles.Contains(role, StringComparer.InvariantCultureIgnoreCase)))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Requirement that accepts either specific scopes (for user tokens) or specific app roles (for M2M tokens).
    /// </summary>
    public class ScopeOrAppRoleRequirement : IAuthorizationRequirement
    {
        public string[] AcceptedScopes { get; }
        public string[] AcceptedRoles { get; }

        public ScopeOrAppRoleRequirement(string[] acceptedScopes, string[] acceptedRoles)
        {
            AcceptedScopes = acceptedScopes ?? Array.Empty<string>();
            AcceptedRoles = acceptedRoles ?? Array.Empty<string>();
        }
    }
}
