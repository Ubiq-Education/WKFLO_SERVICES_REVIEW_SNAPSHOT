using AMAIS.WKFLO.MCP.Application.Apis.Amais.Post;
using Flurl.Http.Testing;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Apis.Amais.Post
{
    public class PostCloneApiTests : IDisposable
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
                    message = "Post cloned.",
                    url = "https://cms.example.com/new-post",
                    newPostClonedName = "New Post",
                    newPostClonedAlias = "new-post-alias"
                }
            });
        }

        [Fact]
        public async Task ClonePostAsync_WhenClearSeoAndSocialMetadataOmitted_SendsFalseInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PostApi(BasePath, "Etc/UTC");

            var result = await api.ClonePostAsync("post-alias", "New Post");

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePostAsync_WhenClearSeoAndSocialMetadataTrue_SendsTrueInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PostApi(BasePath, "Etc/UTC");

            var result = await api.ClonePostAsync("post-alias", "New Post", clearSeoAndSocialMetadata: true);

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = true, versionUnsharedFeatures = false })
                .Times(1);
        }

        [Fact]
        public async Task ClonePostAsync_WhenVersionUnsharedFeaturesTrue_SendsTrueInRequestBody()
        {
            RespondWithSuccess(_httpTest);
            var api = new PostApi(BasePath, "Etc/UTC");

            var result = await api.ClonePostAsync("post-alias", "New Post", versionUnsharedFeatures: true);

            Assert.False(result.IsError);
            _httpTest.ShouldHaveCalled($"{BasePath}/api/v1/post/clone")
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { postAliasToClone = "post-alias", newPostName = "New Post", clearSeoAndSocialMetadata = false, versionUnsharedFeatures = true })
                .Times(1);
        }
    }
}
