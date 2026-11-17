using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace AMAIS.WKFLO.Common.Infrastructure.Middleware
{
    public class UserContextResolver
    {
        private readonly RequestDelegate _next;

        public UserContextResolver(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var userContext = context.RequestServices.GetRequiredService<UserContext>();

            if (context.User.Identity?.IsAuthenticated == true)
            {
                userContext.Claims = context.User.Claims.Select(m => new ClaimContext(m.Type, m.Value)).ToList();

                userContext.UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                     context.User.FindFirst("sub")?.Value ??
                                     string.Empty;
                userContext.Audience = context.User.FindFirst("aud")?.Value ??
                                     string.Empty;

                userContext.GivenName = context.User.FindFirst(ClaimTypes.GivenName)?.Value ??
                                     context.User.FindFirst("givenname")?.Value ??
                                     string.Empty;

                userContext.Surname = context.User.FindFirst(ClaimTypes.Surname)?.Value ??
                                     context.User.FindFirst("surname")?.Value ??
                                     string.Empty;

                userContext.UserName = context.User.FindFirst(ClaimTypes.Name)?.Value ??
                                      context.User.FindFirst("name")?.Value ??
                                      string.Empty;

                userContext.Email = context.User.FindFirst(ClaimTypes.Email)?.Value ??
                                   context.User.FindFirst("email")?.Value ??
                                   string.Empty;

                if (string.IsNullOrEmpty(userContext.Email))
                {
                    if (userContext.UserName.Contains("@"))
                    {
                        userContext.Email = userContext.UserName;
                    }
                }

                // Bug 7527: service-to-service callers (e.g. PetraCMS outside local dev, which authenticates
                // with a Managed Identity) send app-only tokens with no user claims, so the acting user comes
                // in the amais-wkflo-user-email header instead. Only honoured for Entra ID app-only tokens - a
                // real user token always keeps its own identity, and AMAIS-issued JWTs (AI API / MCP Server)
                // never qualify - and AutoUserCreationMiddleware then resolves UserContext.Guid from this
                // email as usual.
                if (string.IsNullOrEmpty(userContext.Email) && IsEntraAppOnlyToken(context.User))
                {
                    var headerEmail = context.Request.Headers[CommonConsts.UserEmail].FirstOrDefault()?.Trim();
                    if (System.Net.Mail.MailAddress.TryCreate(headerEmail, out var parsedHeaderEmail) &&
                        string.Equals(parsedHeaderEmail.Address, headerEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        userContext.Email = headerEmail;
                        if (string.IsNullOrEmpty(userContext.UserName))
                        {
                            userContext.UserName = headerEmail;
                        }
                    }
                }

                // Obtener los roles del usuario
                userContext.Roles = context.User.FindAll(ClaimTypes.Role)
                                  .Select(c => c.Value)
                                  .ToList();

                userContext.RawToken = ExtractRawToken(context);
            }
            else //TODO: NEED TO REVIEW WHAT TO DO FOR CMS Integration when user doesnt exist... ATM I sett default UserId
            {
                userContext.UserId = Guid.Empty.ToString();
                userContext.Guid = Guid.Empty.ToString();
            }

            await _next(context);
        }

        private const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";

        // Entra ID app-only tokens (client credentials / managed identity) carry idtyp=app when that
        // optional claim is emitted, and never carry a delegated "scp" claim (which JwtBearer may map to
        // ScopeClaimType), whereas every delegated user token does. The issuer check comes first because
        // AMAIS-issued JWTs (AmaisJwtScheme in the AI API, the MCP Server's bearer scheme) never carry
        // "scp" either, and are held by end users' browsers - they must never be able to pick an identity
        // via the header. Entra v2 tokens are issued by login.microsoftonline.com/{tid}/v2.0, v1 tokens
        // by sts.windows.net/{tid}/.
        private static bool IsEntraAppOnlyToken(ClaimsPrincipal user)
        {
            var issuer = user.FindFirst("iss")?.Value ?? string.Empty;
            var isEntraIssuer = issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
                || issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase);
            if (!isEntraIssuer)
            {
                return false;
            }

            if (string.Equals(user.FindFirst("idtyp")?.Value, "app", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return user.FindFirst("scp") == null && user.FindFirst(ScopeClaimType) == null;
        }

        private static string ExtractRawToken(HttpContext context)
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Substring("Bearer ".Length).Trim();
            }
            return string.Empty;
        }
    }
}
