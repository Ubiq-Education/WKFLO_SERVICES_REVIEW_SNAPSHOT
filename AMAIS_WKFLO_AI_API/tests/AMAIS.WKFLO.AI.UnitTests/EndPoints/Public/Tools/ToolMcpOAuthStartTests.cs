using AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Tools;
using AMAIS.WKFLO.AI.WebAPI.Infrastructure.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Linq;
using System.Reflection;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints.Public.Tools
{
    /// <summary>
    /// The actual JWT-vs-database authorization logic is <see cref="WebChatAuthorizationAttribute"/>'s own
    /// generic, already-shared mechanism (see its remarks), not anything new introduced by this endpoint —
    /// what's specific and worth asserting here is that <see cref="ToolMcpOAuthStart"/> is actually gated by
    /// <see cref="AuthorizationMethod.AmaisJwtOnly"/> rather than the default
    /// <see cref="AuthorizationMethod.AmaisJwtOrDatabase"/>, since that's precisely what closes the
    /// "share-id-only session with no real user identity" gap for a PerUser-scoped MCP tool.
    /// </summary>
    public class ToolMcpOAuthStartTests
    {
        [Fact]
        public void HandleAsync_IsGatedByAmaisJwtOnly_NotTheDefaultAmaisJwtOrDatabase()
        {
            var method = GetHandleAsyncMethod();

            var attribute = method.GetCustomAttribute<WebChatAuthorizationAttribute>();

            Assert.NotNull(attribute);
            Assert.Equal(AuthorizationMethod.AmaisJwtOnly, GetAuthorizationMethod(attribute));
        }

        [Fact]
        public void HandleAsync_RouteIncludesShareIdAndConnectionIdSegments()
        {
            var method = GetHandleAsyncMethod();

            var route = method.GetCustomAttributes().OfType<IRouteTemplateProvider>().FirstOrDefault();

            Assert.NotNull(route);
            Assert.Equal("/api/v1/agent/{shareId}/tool/{id}/mcp/oauth/start", route.Template);
        }

        private static MethodInfo GetHandleAsyncMethod() =>
            typeof(ToolMcpOAuthStart).GetMethod(nameof(ToolMcpOAuthStart.HandleAsync))
                ?? throw new System.InvalidOperationException($"{nameof(ToolMcpOAuthStart)}.{nameof(ToolMcpOAuthStart.HandleAsync)} not found.");

        private static AuthorizationMethod GetAuthorizationMethod(WebChatAuthorizationAttribute attribute) => attribute.Method;
    }
}
