using AMAIS.WKFLO.MCP.Application.Apis.Amais.Page;
using Flurl.Http.Testing;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Apis.Amais.Page
{
    public class PageCloneApiTests : IDisposable
    {
        private const string BasePath = "https://cms.example.com";

        private readonly HttpTest _httpTest = new();

        public void Dispose() => _httpTest.Dispose();

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
        public async Task ClonePageAsync_WhenClearSeoAndSocialMetadataOmitted_SendsFalseInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PageApi(BasePath, "Etc/UTC");

            var result = await api.ClonePageAsync("page-alias", "New Page");

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePageAsync_WhenClearSeoAndSocialMetadataTrue_SendsTrueInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PageApi(BasePath, "Etc/UTC");

            var result = await api.ClonePageAsync("page-alias", "New Page", clearSeoAndSocialMetadata: true);

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = true, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePageAsync_WhenVersionUnsharedFeaturesTrue_SendsTrueInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PageApi(BasePath, "Etc/UTC");

            var result = await api.ClonePageAsync("page-alias", "New Page", versionUnsharedFeatures: true);

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/page/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { pageAliasToClone = "page-alias", newPageName = "New Page", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = true })
                .Times(1);
        }
    }
}
