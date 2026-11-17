using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentAuthorizationRequirement
{
    public class CheckPublicAgentAuthorizationRequirementQueryHandler : IRequestHandler<CheckPublicAgentAuthorizationRequirementQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly UserContext _userContext;
        private readonly TenantContext _tenantContext;

        public CheckPublicAgentAuthorizationRequirementQueryHandler(IMasterDataDbContext db, UserContext userContext, TenantContext tenantContext)
        {
            _db = db;
            _userContext = userContext;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(CheckPublicAgentAuthorizationRequirementQuery query, CancellationToken cancellationToken)
        {
            // Check AgentChatSetting for security configuration
            var chatSetting = await _db.AgentChatSettings
                .Include(acs => acs.Agent)
                .Where(acs => acs.ShareId == query.ShareId && !acs.IsDeleted && !acs.Agent.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            bool resultAuth = false;

            if (chatSetting != null)
            {
                // Check if the agent exists and its authorization settings
                var agent = chatSetting.Agent;

                if (agent == null)
                {
                    return new NotFoundResult(nameof(agent), "Agent not found");
                }

                // Check security requirements for runnable agents
                if (agent is RunnableAgent runnableAgent2)
                {
                    // Parse SecurityConfiguration to determine authorization requirements
                    if (!string.IsNullOrEmpty(chatSetting.SecurityConfiguration))
                    {
                        try
                        {
                            var securityConfig = JsonConvert.DeserializeObject<AgentSecurityConfiguration>(chatSetting.SecurityConfiguration);

                            // Determine authorization requirement based on configuration
                            resultAuth = AuthorizationRequirement(chatSetting, securityConfig);
                        }
                        catch (JsonException)
                        {
                            // If parsing fails, default to requiring auth for security
                            resultAuth = false;
                        }
                    }

                    // Additional logic: If the agent is shareable and has a ShareId, it might be public
                    if (chatSetting.IsShareable && !string.IsNullOrEmpty(chatSetting.ShareId) && string.IsNullOrEmpty(chatSetting.SecurityConfiguration))
                    {
                        // For shareable agents without explicit security config,
                        // you might want to make them public by default
                        resultAuth = true;
                    }
                }
            }

            return new SuccessResult<bool>(resultAuth);
        }

        private bool AuthorizationRequirement(AgentChatSetting chatSetting, AgentSecurityConfiguration securityConfig)
        {
            // Priority 1: Explicit security configuration
            if (!string.IsNullOrEmpty(chatSetting.SecurityConfiguration))
            {
                switch (securityConfig.Type?.ToLowerInvariant())
                {
                    case "none":
                        return true; // No authentication required
                    case "azure-ad":
                        var requiredScopes = securityConfig.AzureAd.Scopes;
                        requiredScopes = requiredScopes.Where(m => m.StartsWith("api://")).ToArray();

                        if (requiredScopes?.Length > 0)
                        {
                            foreach (var scope in requiredScopes)
                            {
                                var hasRequiredScope = _userContext.Claims.Any(c => c.Type == "http://schemas.microsoft.com/identity/claims/scope" &&
                                        scope == $"api://{securityConfig.AzureAd.ClientId}/{c.Value}");

                                if (!hasRequiredScope)
                                {
                                    return false;
                                }
                            }
                        }

                        return true;
                    case "api-key":

                        if (securityConfig.ApiKey.TenantName == _tenantContext.TenantName && securityConfig.ApiKey.TenantDomains.Any(m => m == _tenantContext.Domain) &&
                            securityConfig.ApiKey.Value == _tenantContext.ApiKey)
                        {
                            return true;
                        }

                        return false;
                    default:
                        return false;
                }
            }

            // Priority 2: Check if agent is shareable (legacy behavior)
            if (chatSetting.IsShareable && !string.IsNullOrEmpty(chatSetting.ShareId) && string.IsNullOrEmpty(chatSetting.SecurityConfiguration))
            {
                // Shareable agents can be public by default unless explicitly restricted
                return true;
            }

            // Default: Authorization
            return false;
        }
    }
}