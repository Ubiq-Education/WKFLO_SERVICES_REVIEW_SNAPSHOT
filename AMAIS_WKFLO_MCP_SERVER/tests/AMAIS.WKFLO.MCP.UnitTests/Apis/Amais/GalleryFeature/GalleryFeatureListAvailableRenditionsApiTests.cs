using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature;
using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http.Testing;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MCP.UnitTests.Apis.Amais.GalleryFeature
{
    public class GalleryFeatureListAvailableRenditionsApiTests : IDisposable
    {
        private const string BasePath = "https://cms.example.com";
        private const string RenditionsUrl = BasePath + "/api/v1/galleryfeature/available/rendition/list";
        private const string PlaceholderSearchTerms = "image";

        private readonly HttpTest _httpTest = new();

        public void Dispose() => _httpTest.Dispose();

        private static GalleryFeatureApi BuildApi() => new(BasePath, "Etc/UTC");

        private static object Rendition(int id) => new
        {
            renditionId = id,
            name = $"rendition-{id}.jpg",
            url = $"https://cms.example.com/api/v1/asset/blob-{id}/1",
            lastUpdatedDate = "2026-01-01T00:00:00+00:00"
        };

        private void RespondWithRenditions(params int[] ids)
        {
            _httpTest.RespondWithJson(new
            {
                resultType = 200,
                data = new { renditions = ids.Select(Rendition).ToArray() }
            });
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WithoutRenditionIds_SendsOnlySearchTermsAndReturnsResponseUnfiltered()
        {
            RespondWithRenditions(1, 2, 3);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync("sports day");

            Assert.False(result.IsError);
            Assert.Equal([1, 2, 3], result.Data.Renditions.Select(r => r.RenditionId));
            Assert.Null(result.Data.NotFoundRenditionIds);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { searchTerms = "sports day" })
                .Times(1);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WithOnlyRenditionIds_SendsPlaceholderSearchTermsAndIds()
        {
            RespondWithRenditions(5243, 5244);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, [5243, 5244]);

            Assert.False(result.IsError);
            Assert.Equal([5243, 5244], result.Data.Renditions.Select(r => r.RenditionId));
            Assert.Empty(result.Data.NotFoundRenditionIds);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithVerb(HttpMethod.Post)
                .WithRequestJson(new { searchTerms = PlaceholderSearchTerms, renditionIds = new[] { 5243, 5244 } })
                .Times(1);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WithSearchTermsAndRenditionIds_SendsGivenSearchTerms()
        {
            RespondWithRenditions(7);

            await BuildApi().ListAvailableGalleryFeatureRenditionsAsync("logo", [7]);

            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithRequestJson(new { searchTerms = "logo", renditionIds = new[] { 7 } })
                .Times(1);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WithMoreThanTenIds_BatchesByTenAndMergesInRequestedOrder()
        {
            var ids = Enumerable.Range(1, 25).ToArray();
            RespondWithRenditions(ids[..10]);
            RespondWithRenditions(ids[10..20]);
            RespondWithRenditions(ids[20..]);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, ids);

            Assert.False(result.IsError);
            Assert.Equal(ids, result.Data.Renditions.Select(r => r.RenditionId));
            Assert.Empty(result.Data.NotFoundRenditionIds);
            _httpTest.ShouldHaveCalled(RenditionsUrl).Times(3);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithRequestJson(new { searchTerms = PlaceholderSearchTerms, renditionIds = ids[..10] }).Times(1);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithRequestJson(new { searchTerms = PlaceholderSearchTerms, renditionIds = ids[10..20] }).Times(1);
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithRequestJson(new { searchTerms = PlaceholderSearchTerms, renditionIds = ids[20..] }).Times(1);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WhenCmsIgnoresRenditionIds_FiltersOutUnrelatedRenditionsAndReportsMissingIds()
        {
            // An older CMS ignores renditionIds and returns keyword matches for the placeholder search terms.
            RespondWithRenditions(100, 101, 5244, 102);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, [5243, 5244, 6815]);

            Assert.False(result.IsError);
            Assert.Equal([5244], result.Data.Renditions.Select(r => r.RenditionId));
            Assert.Equal([5243, 6815], result.Data.NotFoundRenditionIds);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WhenCmsReturnsNoRenditions_ReportsAllIdsAsNotFound()
        {
            _httpTest.RespondWithJson(new { resultType = 200, data = new { renditions = Array.Empty<object>() } });

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, [1, 2]);

            Assert.False(result.IsError);
            Assert.Empty(result.Data.Renditions);
            Assert.Equal([1, 2], result.Data.NotFoundRenditionIds);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WithDuplicateAndNonPositiveIds_SendsDistinctPositiveIdsOnly()
        {
            RespondWithRenditions(3);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, [3, 0, -1, 3]);

            Assert.Equal([3], result.Data.Renditions.Select(r => r.RenditionId));
            _httpTest.ShouldHaveCalled(RenditionsUrl)
                .WithRequestJson(new { searchTerms = PlaceholderSearchTerms, renditionIds = new[] { 3 } })
                .Times(1);
        }

        [Fact]
        public async Task ListAvailableGalleryFeatureRenditionsAsync_WhenABatchFails_ReturnsErrorAndStopsBatching()
        {
            _httpTest.RespondWith("Search terms is required.", 400);

            var result = await BuildApi().ListAvailableGalleryFeatureRenditionsAsync(null, Enumerable.Range(1, 15));

            Assert.True(result.IsError);
            Assert.Equal(400, result.ResultType);
            Assert.Contains("Search terms is required.", result.ErrorMessage);
            _httpTest.ShouldHaveCalled(RenditionsUrl).Times(1);
        }

        [Fact]
        public void GalleryImageResult_FromCmsWithoutUrlAndName_DeserializesWithNullsAndSerializesWithoutThem()
        {
            var image = JsonConvert.DeserializeObject<GalleryImageResult>("{\"renditionId\":5243,\"galleryId\":12,\"width\":800}");

            Assert.Equal(5243, image.RenditionId);
            Assert.Null(image.Url);
            Assert.Null(image.Name);

            var json = JsonConvert.SerializeObject(image);
            Assert.DoesNotContain("\"url\"", json);
            Assert.DoesNotContain("\"name\"", json);
        }

        [Fact]
        public void GalleryImageResult_FromCmsWithUrlAndName_RoundTripsThem()
        {
            var image = JsonConvert.DeserializeObject<GalleryImageResult>(
                "{\"renditionId\":5243,\"galleryId\":12,\"url\":\"https://cms.example.com/api/v1/asset/blob/1\",\"name\":\"photo.jpg\"}");

            Assert.Equal("https://cms.example.com/api/v1/asset/blob/1", image.Url);
            Assert.Equal("photo.jpg", image.Name);

            var json = JsonConvert.SerializeObject(image);
            Assert.Contains("\"url\":\"https://cms.example.com/api/v1/asset/blob/1\"", json);
            Assert.Contains("\"name\":\"photo.jpg\"", json);
        }
    }
}
