using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.Models.Config;
using Flurl.Http.Content;
using Flurl.Http.Testing;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    public class AzureGptImageEditServiceTests
    {
        [Fact]
        public async Task EditImageAsync_BytesOverload_ThrowsWhenEndpointMissing()
        {
            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = null,
                DeploymentName = "gpt-image-1"
            });

            var ex = await Assert.ThrowsAsync<ImageEditProviderException>(() =>
                service.EditImageAsync(new byte[] { 1, 2, 3 }, "test", cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ImageEditFailureKind.NotConfigured, ex.Kind);
        }

        [Fact]
        public async Task EditImageAsync_BytesOverload_ThrowsWhenBothDeploymentNameAndModelAreNull()
        {
            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = null,
                Model = null
            });

            var ex = await Assert.ThrowsAsync<ImageEditProviderException>(() =>
                service.EditImageAsync(new byte[] { 1, 2, 3 }, "test", cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ImageEditFailureKind.NotConfigured, ex.Kind);
        }

        [Theory]
        [InlineData("", "gpt-image-1", "gpt-image-1")]
        [InlineData("   ", "gpt-image-1", "gpt-image-1")]
        [InlineData("https://fake.openai.azure.com", "", "")]
        [InlineData("https://fake.openai.azure.com", " ", null)]
        public async Task EditImageAsync_BytesOverload_TreatsBlankConfigAsMissing(string endpoint, string deploymentName, string model)
        {
            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = endpoint,
                DeploymentName = deploymentName,
                Model = model
            });

            var ex = await Assert.ThrowsAsync<ImageEditProviderException>(() =>
                service.EditImageAsync(new byte[] { 1, 2, 3 }, "test", cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ImageEditFailureKind.NotConfigured, ex.Kind);
        }

        [Fact]
        public async Task EditImageAsync_BytesOverload_FallsBackToModel_WhenDeploymentNameIsBlank()
        {
            using var httpTest = new HttpTest();
            httpTest.RespondWith("error", 500);

            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "  ",
                Model = "gpt-image-1"
            });

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.EditImageAsync(new byte[] { 1, 2, 3 }, "test", cancellationToken: TestContext.Current.CancellationToken));

            httpTest.ShouldHaveCalled("https://fake.openai.azure.com/openai/deployments/gpt-image-1/images/edits*");
        }

        [Fact]
        public async Task EditImageAsync_BytesOverload_FallsBackToModel_WhenDeploymentNameIsNull()
        {
            // Deployment resolves from Model when DeploymentName is not set.
            // The call still fails (fake endpoint), but must NOT throw the config InvalidOperationException.
            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = null,
                Model = "gpt-image-1"
            });

            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                service.EditImageAsync(new byte[] { 1, 2, 3 }, "test", cancellationToken: TestContext.Current.CancellationToken));

            Assert.IsNotType<ImageEditProviderException>(ex);
        }

        [Fact]
        public async Task EditImageAsync_UrlOverload_DelegatesToBytesOverload_ThrowsOnDownloadFailure()
        {
            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "gpt-image-1"
            });

            // No Flurl HttpTest → real HTTP call to non-existent URL → download fails
            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.EditImageAsync("https://nonexistent.invalid/image.png", "test", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideProvided_SendsOverrideInsteadOfConfigDefault()
        {
            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1, 2, 3 }) } } });

            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "gpt-image-1",
                Quality = "high"
            });

            await service.EditImageAsync(new byte[] { 1, 2, 3, 4 }, "test", quality: "low", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("low", GetCapturedStringPart(httpTest, "quality"));
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideOmitted_FallsBackToConfigDefault()
        {
            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1, 2, 3 }) } } });

            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "gpt-image-1",
                Quality = "medium"
            });

            await service.EditImageAsync(new byte[] { 1, 2, 3, 4 }, "test", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("medium", GetCapturedStringPart(httpTest, "quality"));
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideIsEmptyString_FallsBackToConfigDefault()
        {
            // An LLM tool call can send "" instead of omitting an optional parameter; that must not
            // be treated as an explicit override, or it silently discards the tenant's configured quality.
            using var httpTest = new HttpTest();
            httpTest.RespondWithJson(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1, 2, 3 }) } } });

            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "gpt-image-1",
                Quality = "medium"
            });

            await service.EditImageAsync(new byte[] { 1, 2, 3, 4 }, "test", quality: "", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("medium", GetCapturedStringPart(httpTest, "quality"));
        }

        private static async Task<HttpTest> SendAsync(byte[] input, string aspectRatio = null, string configRatio = null, string apiVersion = "2025-04-01-preview")
        {
            var httpTest = new HttpTest();
            httpTest.RespondWithJson(new { data = new[] { new { b64_json = Convert.ToBase64String(new byte[] { 1, 2, 3 }) } } });

            var service = new AzureGptImageEditService(new ImageGenerationToolConfig
            {
                Provider = "azure",
                ApiKey = "fake-key",
                Endpoint = "https://fake.openai.azure.com",
                DeploymentName = "gpt-image-1",
                Quality = "high",
                AspectRatio = configRatio,
                ApiVersion = apiVersion
            });

            await service.EditImageAsync(input, "test", aspectRatio: aspectRatio, cancellationToken: TestContext.Current.CancellationToken);
            return httpTest;
        }

        [Theory]
        [InlineData("2025-04-01-preview", true)]
        [InlineData("2025-05-01-preview", true)]
        [InlineData("2025-04-01", false)]
        [InlineData("2025-03-01-preview", false)]
        [InlineData("garbage", false)]
        public async Task EditImageAsync_SizeIsSentOnlyForSupportedApiVersions(string apiVersion, bool expectSize)
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(3024, 4032), apiVersion: apiVersion);

            Assert.Equal(expectSize, HasPart(httpTest, "size"));
            Assert.Contains($"api-version={apiVersion}", httpTest.CallLog[0].Request.Url.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task EditImageAsync_BlankApiVersion_UsesPreviewDefaultInUrlAndSendsSize(string apiVersion)
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(3024, 4032), apiVersion: apiVersion);

            Assert.Contains("api-version=2025-04-01-preview", httpTest.CallLog[0].Request.Url.ToString());
            Assert.Equal("1024x1536", GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public void ToolConfigDefaultApiVersion_IsPreviewDefault() =>
            Assert.Equal("2025-04-01-preview", new ImageGenerationToolConfig().ApiVersion);

        private static bool HasPart(HttpTest httpTest, string name)
        {
            var multipart = (MultipartContent)httpTest.CallLog[0].HttpRequestMessage.Content;
            return multipart.Any(p => p.Headers.ContentDisposition?.Name == name);
        }

        [Theory]
        [InlineData(3024, 4032, "1024x1536")]
        [InlineData(4032, 3024, "1536x1024")]
        [InlineData(1080, 1080, "1024x1024")]
        public async Task EditImageAsync_DetectedInputRatio_SendsMatchingSize(int w, int h, string expected)
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(w, h));

            Assert.Equal(expected, GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public async Task EditImageAsync_ConfigRatioWinsOverDetected()
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(3024, 4032), configRatio: "16:9");

            Assert.Equal("1536x1024", GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public async Task EditImageAsync_ExplicitRatioOverride_IsMappedToNearestSizeAndWinsOverDetected()
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(3024, 4032), aspectRatio: "16:9");

            Assert.Equal("1536x1024", GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public async Task EditImageAsync_UnparseableOverride_FallsBackToDetected()
        {
            using var httpTest = await SendAsync(TestImageBytes.Png(3024, 4032), aspectRatio: "wide");

            Assert.Equal("1024x1536", GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public async Task EditImageAsync_UndetectableInput_UsesConfigRatioAsFallback()
        {
            using var httpTest = await SendAsync(new byte[] { 1, 2, 3, 4 }, configRatio: "9:16");

            Assert.Equal("1024x1536", GetCapturedStringPart(httpTest, "size"));
        }

        [Fact]
        public async Task EditImageAsync_UndetectableInputAndNoRatio_OmitsSize()
        {
            using var httpTest = await SendAsync(new byte[] { 1, 2, 3, 4 });

            Assert.False(HasPart(httpTest, "size"));
            Assert.False(HasPart(httpTest, "aspectRatio"));
        }

        private static string GetCapturedStringPart(HttpTest httpTest, string partName)
        {
            var multipart = (MultipartContent)httpTest.CallLog[0].HttpRequestMessage.Content;
            var part = (CapturedStringContent)multipart.First(p => p.Headers.ContentDisposition?.Name == partName);
            return part.Content;
        }
    }
}
