using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.MCP.Application.Tools.Amais.GalleryFeature;
using Flurl.Http.Testing;
using Microsoft.AspNetCore.Http;
using Moq;
using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Tools.Amais.GalleryFeature
{
    public class AmaisGalleryFeatureToolsListAllRenditionsTests : IDisposable
    {
        private const string CmsBaseAddress = "https://cms.example.com";
        private const string RenditionsUrl = CmsBaseAddress + "/api/v1/galleryfeature/available/rendition/list";

        private readonly HttpTest _httpTest = new();
        private readonly string _previousUserAgentHeader;

        public AmaisGalleryFeatureToolsListAllRenditionsTests()
        {
            _previousUserAgentHeader = Environment.GetEnvironmentVariable("USER_AGENT_HEADER");

            // Avoids ArgumentNullException from HttpRequestHeaders.Add("User-Agent", null)
            // when the env var isn't configured in the test host.
            Environment.SetEnvironmentVariable("USER_AGENT_HEADER", "test-agent");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USER_AGENT_HEADER", _previousUserAgentHeader);
            _httpTest.Dispose();
        }

        private static AmaisGalleryFeatureTools BuildTools()
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

            return new AmaisGalleryFeatureTools(httpClientFactoryMock.Object, httpContextAccessorMock.Object, Mock.Of<IDateTimeProvider>());
        }

        private void RespondWithRendition(int id)
        {
            _httpTest.RespondWithJson(new
            {
                resultType = 200,
                data = new
                {
                    renditions = new[]
                    {
                        new { renditionId = id, name = $"rendition-{id}.jpg", url = $"https://cms.example.com/api/v1/asset/blob-{id}/1", lastUpdatedDate = "2026-01-01T00:00:00+00:00" }
                    }
                }
            });
        }

        [Fact]
        public async Task ListGalleryRenditions_WithoutSearchTermsOrRenditionIds_ReturnsErrorWithoutCallingCms()
        {
            var result = await BuildTools().ListGalleryRenditions();

            Assert.StartsWith("Error:", result.Text);
            _httpTest.ShouldNotHaveMadeACall();
        }

        [Fact]
        public async Task ListGalleryRenditions_WithSearchTermsOnly_ForwardsSearchTermsOnly()
        {
            RespondWithRendition(1);

            var result = await BuildTools().ListGalleryRenditions("image");

            Assert.Contains("\"renditionId\":1", result.Text);
            Assert.DoesNotContain("notFoundRenditionIds", result.Text);
            Assert.DoesNotContain("\"message\"", result.Text);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { searchTerms = "image" })
                .Times(1);
        }

        [Fact]
        public async Task ListGalleryRenditions_WithRenditionIdsOnly_ReturnsResolvedUrlsAndReportsMissingIds()
        {
            RespondWithRendition(5243);

            var result = await BuildTools().ListGalleryRenditions(renditionIds: [5243, 6815]);

            Assert.Contains("\"renditionId\":5243", result.Text);
            Assert.Contains("https://cms.example.com/api/v1/asset/blob-5243/1", result.Text);
            Assert.Contains("\"notFoundRenditionIds\":[6815]", result.Text);
            Assert.Contains("6815", result.Text);
            Assert.Contains("\"message\"", result.Text);
        }

        [Fact]
        public async Task ListGalleryRenditions_WhenAllRenditionIdsResolved_DoesNotAddMessage()
        {
            RespondWithRendition(5243);

            var result = await BuildTools().ListGalleryRenditions(renditionIds: [5243]);

            Assert.Contains("\"notFoundRenditionIds\":[]", result.Text);
            Assert.DoesNotContain("\"message\"", result.Text);
        }
    }
}
