using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using Flurl.Http;
using Google.GenAI;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    public class ImageEditErrorClassifierTests
    {
        [Theory]
        [InlineData(429, "RESOURCE_EXHAUSTED", "Too many requests", ImageEditFailureKind.RateLimited)]
        [InlineData(429, "RESOURCE_EXHAUSTED", "You exceeded your current quota, please check your plan and billing details", ImageEditFailureKind.RateLimited)]
        [InlineData(429, "RESOURCE_EXHAUSTED", "Quota exceeded for metric generate_content_free_tier_requests, quotaId: GenerateRequestsPerDayPerProjectPerModel-FreeTier", ImageEditFailureKind.QuotaExceeded)]
        [InlineData(429, "RESOURCE_EXHAUSTED", "Billing is disabled for this project", ImageEditFailureKind.QuotaExceeded)]
        [InlineData(400, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key.", ImageEditFailureKind.NotConfigured)]
        [InlineData(503, "UNAVAILABLE", "The model is overloaded. Please try again later.", ImageEditFailureKind.Overloaded)]
        [InlineData(500, "INTERNAL", "Internal error", ImageEditFailureKind.Overloaded)]
        [InlineData(504, "DEADLINE_EXCEEDED", "Deadline expired", ImageEditFailureKind.Overloaded)]
        [InlineData(408, null, "Request timeout", ImageEditFailureKind.Overloaded)]
        [InlineData(502, null, "Bad gateway", ImageEditFailureKind.Overloaded)]
        [InlineData(502, null, "Upstream rejected the API key", ImageEditFailureKind.Overloaded)]
        [InlineData(400, "INVALID_ARGUMENT", "Unsupported image", ImageEditFailureKind.InvalidRequest)]
        [InlineData(400, "FAILED_PRECONDITION", "Prompt blocked due to safety", ImageEditFailureKind.SafetyBlocked)]
        [InlineData(401, "UNAUTHENTICATED", "Bad key", ImageEditFailureKind.NotConfigured)]
        [InlineData(403, "PERMISSION_DENIED", "API key not valid", ImageEditFailureKind.NotConfigured)]
        [InlineData(404, "NOT_FOUND", "Model not found", ImageEditFailureKind.NotConfigured)]
        [InlineData(418, "TEAPOT", "Something else", ImageEditFailureKind.Other)]
        public async Task ClassifyAsync_GoogleApiException_MapsStatus(int statusCode, string status, string message, ImageEditFailureKind expected)
        {
            var ex = new ApiException(message, statusCode, status, null);

            Assert.Equal(expected, await ImageEditErrorClassifier.ClassifyAsync(ex));
        }

        [Fact]
        public async Task DescribeAsync_GoogleApiException_CapturesCodeStatusAndMessage()
        {
            var info = await ImageEditErrorClassifier.DescribeAsync(new ApiException("quota hit", 429, "RESOURCE_EXHAUSTED", null));

            Assert.Equal(429, info.StatusCode);
            Assert.Equal("RESOURCE_EXHAUSTED", info.Status);
            Assert.Contains("quota hit", info.Message);
        }

        [Theory]
        [InlineData(ImageEditFailureKind.SafetyBlocked)]
        [InlineData(ImageEditFailureKind.Other)]
        [InlineData(ImageEditFailureKind.RateLimited)]
        public async Task DescribeAsync_ImageEditProviderException_UsesItsKindAndDetails(ImageEditFailureKind kind)
        {
            var ex = new ImageEditProviderException(kind, "google", "raw", "SAFETY", new[] { "IMAGE_SAFETY" });

            var info = await ImageEditErrorClassifier.DescribeAsync(ex);

            Assert.Equal(kind, info.Kind);
            Assert.Equal("SAFETY", info.BlockReason);
            Assert.Equal("IMAGE_SAFETY", info.FinishReasons);
        }

        [Theory]
        [InlineData(typeof(TaskCanceledException), ImageEditFailureKind.Overloaded)]
        [InlineData(typeof(TimeoutException), ImageEditFailureKind.Overloaded)]
        [InlineData(typeof(InvalidOperationException), ImageEditFailureKind.Other)]
        public async Task ClassifyAsync_OtherExceptions(Type type, ImageEditFailureKind expected)
        {
            var ex = (Exception)Activator.CreateInstance(type, "boom");

            Assert.Equal(expected, await ImageEditErrorClassifier.ClassifyAsync(ex));
        }

        [Theory]
        [InlineData(429, "{\"error\":{\"message\":\"Rate limit reached\"}}", ImageEditFailureKind.RateLimited)]
        [InlineData(429, "{\"error\":{\"code\":\"insufficient_quota\"}}", ImageEditFailureKind.QuotaExceeded)]
        [InlineData(429, "{\"error\":{\"message\":\"Requests exceeded. To increase quota see https://aka.ms/oai/quotaincrease\"}}", ImageEditFailureKind.RateLimited)]
        [InlineData(503, "{}", ImageEditFailureKind.Overloaded)]
        [InlineData(400, "{\"error\":{\"code\":\"content_policy_violation\"}}", ImageEditFailureKind.SafetyBlocked)]
        [InlineData(400, "{\"error\":{\"code\":\"bad_param\"}}", ImageEditFailureKind.InvalidRequest)]
        [InlineData(401, "{}", ImageEditFailureKind.NotConfigured)]
        public async Task ClassifyAsync_FlurlHttpException_MapsStatusAndBody(int statusCode, string body, ImageEditFailureKind expected)
        {
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            httpTest.RespondWith(body, statusCode);

            FlurlHttpException caught = null;
            try
            {
                await "https://azure.example.com/images/edits".PostStringAsync("x", cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (FlurlHttpException ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.Equal(expected, await ImageEditErrorClassifier.ClassifyAsync(caught));
        }

        [Fact]
        public async Task ClassifyAsync_FlurlHttpTimeoutException_IsOverloaded()
        {
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            httpTest.SimulateTimeout();

            Exception caught = null;
            try
            {
                await "https://azure.example.com/images/edits".PostStringAsync("x", cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (FlurlHttpTimeoutException ex)
            {
                caught = ex;
            }

            Assert.NotNull(caught);
            Assert.Equal(ImageEditFailureKind.Overloaded, await ImageEditErrorClassifier.ClassifyAsync(caught));
        }
    }
}
