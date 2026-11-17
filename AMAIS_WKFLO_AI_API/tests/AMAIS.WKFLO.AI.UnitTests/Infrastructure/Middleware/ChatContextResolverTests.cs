using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.Middleware
{
    public class ChatContextResolverTests
    {
        private static (ChatContextResolver middleware, ChatContext chatContext, HttpContext httpContext) BuildPipeline(
            ClaimsPrincipal user = null)
        {
            var chatContext = new ChatContext();

            var services = new ServiceCollection();
            services.AddSingleton(chatContext);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider,
                User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
            };

            var middleware = new ChatContextResolver(_ => Task.CompletedTask);

            return (middleware, chatContext, httpContext);
        }

        // ── SessionId / PagePath ─────────────────────────────────────────────

        [Fact]
        public async Task Invoke_WithSessionIdHeader_SetsSessionId()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-session-id"] = "session-abc";

            await middleware.Invoke(httpContext);

            Assert.Equal("session-abc", chatContext.SessionId);
        }

        [Fact]
        public async Task Invoke_WithPagePathHeader_SetsPagePath()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-page-path"] = "/home";

            await middleware.Invoke(httpContext);

            Assert.Equal("/home", chatContext.PagePath);
        }

        [Fact]
        public async Task Invoke_WithPageIdHeader_SetsPageId()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-page-id"] = " 1234 ";

            await middleware.Invoke(httpContext);

            Assert.Equal("1234", chatContext.PageId);
        }

        [Fact]
        public async Task Invoke_WithoutPageIdHeader_LeavesPageIdNull()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-page-path"] = "/home";

            await middleware.Invoke(httpContext);

            Assert.Null(chatContext.PageId);
        }

        [Fact]
        public async Task Invoke_WithSessionIdAndPagePath_SetsBoth()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-session-id"] = "session-xyz";
            httpContext.Request.Headers["amais-wkflo-page-path"] = "/about";

            await middleware.Invoke(httpContext);

            Assert.Equal("session-xyz", chatContext.SessionId);
            Assert.Equal("/about", chatContext.PagePath);
        }

        // ── FeatureId / FeatureType ──────────────────────────────────────────

        [Fact]
        public async Task Invoke_WithFeatureIdAndFeatureTypeHeaders_SetsBoth()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-feature-id"] = "42";
            httpContext.Request.Headers["amais-wkflo-feature-type"] = "Text";

            await middleware.Invoke(httpContext);

            Assert.Equal("42", chatContext.FeatureId);
            Assert.Equal("Text", chatContext.FeatureType);
        }

        [Fact]
        public async Task Invoke_WithoutFeatureHeaders_LeavesFeatureIdAndFeatureTypeNull()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();

            await middleware.Invoke(httpContext);

            Assert.Null(chatContext.FeatureId);
            Assert.Null(chatContext.FeatureType);
        }

        [Fact]
        public async Task Invoke_WithFeatureNameHeader_SetsFeatureName()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-feature-name"] = "Homepage Hero";

            await middleware.Invoke(httpContext);

            Assert.Equal("Homepage Hero", chatContext.FeatureName);
        }

        [Fact]
        public async Task Invoke_WithoutFeatureNameHeader_LeavesFeatureNameNull()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();

            await middleware.Invoke(httpContext);

            Assert.Null(chatContext.FeatureName);
        }

        // ── AmaisAvailableEndpoints ──────────────────────────────────────────

        [Fact]
        public async Task Invoke_WithAmaisAvailableEndpointsHeader_SetsAmaisAvailableEndpoints()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-amais-endpoints"] = "WelcomeMessage, CreatePost,ClonePost";

            await middleware.Invoke(httpContext);

            Assert.Equal(["WelcomeMessage", "CreatePost", "ClonePost"], chatContext.AmaisAvailableEndpoints);
        }

        [Fact]
        public async Task Invoke_WithoutAmaisAvailableEndpointsHeader_LeavesAmaisAvailableEndpointsNull()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();

            await middleware.Invoke(httpContext);

            Assert.Null(chatContext.AmaisAvailableEndpoints);
        }

        // ── IsTestChat ────────────────────────────────────────────────────────

        [Fact]
        public async Task Invoke_WithIsTestChatHeaderTrue_SetsIsTestChatTrue()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-is-test-chat"] = "true";

            await middleware.Invoke(httpContext);

            Assert.True(chatContext.IsTestChat);
        }

        [Fact]
        public async Task Invoke_WithoutIsTestChatHeader_LeavesIsTestChatFalse()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();

            await middleware.Invoke(httpContext);

            Assert.False(chatContext.IsTestChat);
        }

        [Fact]
        public async Task Invoke_WithIsTestChatHeaderFalse_LeavesIsTestChatFalse()
        {
            var (middleware, chatContext, httpContext) = BuildPipeline();
            httpContext.Request.Headers["amais-wkflo-is-test-chat"] = "false";

            await middleware.Invoke(httpContext);

            Assert.False(chatContext.IsTestChat);
        }

        [Fact]
        public async Task Invoke_WithIsTestChatHeaderAlreadyTrueOnContext_NeverRevertsToFalse()
        {
            // Mirrors the "once true, never revert" contract enforced when the Conversation row is
            // persisted — the resolver itself must never clear a value already set earlier in scope.
            var (middleware, chatContext, httpContext) = BuildPipeline();
            chatContext.IsTestChat = true;

            await middleware.Invoke(httpContext);

            Assert.True(chatContext.IsTestChat);
        }
    }
}
