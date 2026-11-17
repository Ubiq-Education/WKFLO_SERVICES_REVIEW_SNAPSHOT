using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.Page;
using Flurl.Http.Testing;
using Microsoft.AspNetCore.Http;
using Moq;
using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Tools.Amais.Page
{
    public class AmaisPageToolsCloneTests : IDisposable
    {
        private const string CmsBaseAddress = "https://cms.example.com";

        private readonly HttpTest _httpTest = new();

        public AmaisPageToolsCloneTests()
        {
            // Avoids ArgumentNullException from HttpRequestHeaders.Add("User-Agent", null)
            // when the env var isn't configured in the test host.
            Environment.SetEnvironmentVariable("USER_AGENT_HEADER", "test-agent");
        }

        public void Dispose() => _httpTest.Dispose();

        private static AmaisPageTools BuildTools()
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

            return new AmaisPageTools(httpClientFactoryMock.Object, httpContextAccessorMock.Object, Mock.Of<IDateTimeProvider>());
        }

        private static void RespondWithSuccess(HttpTest httpTest)
        {
            httpTest.RespondWithJson(new
            {
                resultType = 200,
                data = new
                {
                    message = "Page cloned.",
                    url = "https://cms.example.com/new-page",
                    newPageClonedName = "New Page",
                    newPageClonedAlias = "new-page-alias"
                }
            });
        }

        [Fact]
        public async Task ClonePage_WhenClearSeoAndSocialMetadataOmitted_ForwardsFalseToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePage("page-alias", "New Page");

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePage_WhenClearSeoAndSocialMetadataTrue_ForwardsTrueToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePage("page-alias", "New Page", clearSeoAndSocialMetadata: true);

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = true, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePage_WhenVersionUnsharedFeaturesTrue_ForwardsTrueToApiCall()
        {
            RespondWithSuccess(_httpTest);
            var tools = BuildTools();

            await tools.ClonePage("page-alias", "New Page", versionUnsharedFeatures: true);

            _httpTest.ShouldHaveCalled($"{CmsBaseAddress}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = true })
                .Times(1);
        }
    }
}
