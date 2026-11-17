using AMAIS.WKFLO.AI.Application.Agents.Queries.CheckPublicAgentAuthorizationRequirement;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters
{
    public enum AuthorizationMethod
    {
        AmaisJwtOnly,
        DatabaseOnly,
        AmaisJwtOrDatabase,
        AmaisJwtAndDatabase
    }

    public class WebChatAuthorizationAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly AuthorizationMethod _authorizationMethod;

        public WebChatAuthorizationAttribute(AuthorizationMethod authorizationMethod = AuthorizationMethod.AmaisJwtOrDatabase)
        {
            _authorizationMethod = authorizationMethod;
        }

        /// <summary>How the endpoint is authorized; read by the OpenAPI operation transformer.</summary>
        public AuthorizationMethod Method => _authorizationMethod;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            bool jwtValid = false;
            bool dbValid = false;

            // Verificar JWT si es necesario
            if (_authorizationMethod == AuthorizationMethod.AmaisJwtOnly ||
                _authorizationMethod == AuthorizationMethod.AmaisJwtOrDatabase ||
                _authorizationMethod == AuthorizationMethod.AmaisJwtAndDatabase)
            {
                jwtValid = await TryAmaisJwtAuthenticationAsync(context);

                if (_authorizationMethod == AuthorizationMethod.AmaisJwtOnly)
                {
                    if (!jwtValid)
                    {
                        context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
                    }
                    return;
                }
            }

            // Verificar base de datos si es necesario
            if (_authorizationMethod == AuthorizationMethod.DatabaseOnly ||
                _authorizationMethod == AuthorizationMethod.AmaisJwtOrDatabase ||
                _authorizationMethod == AuthorizationMethod.AmaisJwtAndDatabase)
            {
                dbValid = await CheckDatabaseAuthorizationAsync(context);

                if (_authorizationMethod == AuthorizationMethod.DatabaseOnly)
                {
                    if (!dbValid)
                    {
                        context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
                    }
                    return;
                }
            }

            // Aplicar lógica combinada
            switch (_authorizationMethod)
            {
                case AuthorizationMethod.AmaisJwtOrDatabase:
                    if (!jwtValid && !dbValid)
                    {
                        context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
                    }
                    break;

                case AuthorizationMethod.AmaisJwtAndDatabase:
                    if (!jwtValid || !dbValid)
                    {
                        context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
                    }
                    break;
            }
        }

        private async Task<bool> TryAmaisJwtAuthenticationAsync(AuthorizationFilterContext context)
        {
            try
            {
                var authenticateResult = await context.HttpContext.AuthenticateAsync(AmaisJwtConst.SchemeName);

                if (authenticateResult.Succeeded && authenticateResult.Principal?.Identity?.IsAuthenticated == true)
                {
                    context.HttpContext.User = authenticateResult.Principal;
                    return true;
                }
            }
            catch (Exception ex)
            {
                var logger = context.HttpContext.RequestServices.GetService<ILogger<WebChatAuthorizationAttribute>>();
                logger?.LogDebug(ex, "Error during custom JWT authentication");
            }

            return false;
        }

        private async Task<bool> CheckDatabaseAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.RouteData.Values.TryGetValue("shareId", out var shareId))
            {
                var agentSharableContext = context.HttpContext.RequestServices.GetRequiredService<AgentSharableContext>();
                agentSharableContext.ShareId = shareId?.ToString();

                var mediator = context.HttpContext.RequestServices.GetRequiredService<AMAIS.WKFLO.Common.Mediator.IWkfloMediator>();
                var query = new CheckPublicAgentAuthorizationRequirementQuery { ShareId = agentSharableContext.ShareId };
                var result = await mediator.Send(query);

                return result is SuccessResult<bool> successResult && successResult.Data;
            }

            return false;
        }
    }
}
