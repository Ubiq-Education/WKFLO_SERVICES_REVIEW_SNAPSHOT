using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.Post;
using Flurl.Http.Testing;
using Microsoft.AspNetCore.Http;
using Moq;
using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Tools.Amais.Post
{
    public class AmaisBlogPostToolsCloneTests : IDisposable
    {
        private const string CmsBaseAddress = "https://cms.example.com";

        private readonly HttpTest _httpTest = new();

        public AmaisBlogPostToolsCloneTests()
        {
            // Avoids ArgumentNullException from HttpRequestHeaders.Add("User-Agent", null)
            // when the env var isn't configured in the test host.
            Environment.SetEnvironmentVariable("USER_AGENT_HEADER", "test-agent");
        }

        public void Dispose() => _httpTest.Dispose();

        private static AmaisBlogPostTools BuildTools()
        {
            var identity = new ClaimsIdentity([new Claim("aud", CmsBaseAddress)], "TestAuthType");
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            };
            httpContext.Request.Headers.Authorization = "Bearer test-token";

            var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock.Setup(f => f.CreateClient("AmaisApi")).Returns(new HttpClient());

            return new AmaisBlogPostTools(httpClientFactoryMock.Object, httpContextAccessorMock.Object, Mock.Of<IDateTimeProvider>());
        }

        private static void RespondWithSuccess(HttpTest httpTest)
        {
            httpTest.RespondWithJson(new
            {
                resultType = 200,
                data = new
                {
                    message = "Post cloned.",
                    url = "https://cms.example.com/new-post",
                    newPostClonedName = "New Post",
                    newPostClonedAlias = "new-post-alias"
                }
            });
        }

        [Fact]
        public async Task ClonePost_WhenClearSeoAndSocialMetadataOmitted_ForwardsFalseToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePost("post-alias", "New Post");

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePost_WhenClearSeoAndSocialMetadataTrue_ForwardsTrueToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePost("post-alias", "New Post", clearSeoAndSocialMetadata: true);

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = true, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePost_WhenVersionUnsharedFeaturesTrue_ForwardsTrueToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePost("post-alias", "New Post", versionUnsharedFeatures: true);

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = true })
                .Times(1);
        }
    }
}
