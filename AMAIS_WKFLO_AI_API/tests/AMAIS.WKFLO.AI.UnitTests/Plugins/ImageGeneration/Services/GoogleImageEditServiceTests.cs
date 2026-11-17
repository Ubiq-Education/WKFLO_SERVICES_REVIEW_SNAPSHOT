using AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.Models.Config;
using Google.GenAI.Types;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    public class GoogleImageEditServiceTests
    {
        // MIME-type sniffing itself is covered by ImageMimeTypeHelperTests — GoogleImageEditService
        // just delegates to ImageMimeTypeHelper.DetectFromBytes now, shared with
        // LoadSiblingAgentContextBySessionQueryHandler (US 7078).

        // ── EditImageAsync ─────────────────────────────────────────────────

        private static readonly byte[] FakeImageBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };

        private static GenerateContentResponse BuildResponse(byte[] imageBytes, string mimeType = "image/png", int promptTokens = 0, int candidatesTokens = 0) => new()
        {
            Candidates = new List<Candidate>
            {
                new Candidate
                {
                    Content = new Content
                    {
                        Parts = new List<Part>
                        {
                            new Part { InlineData = new Blob { Data = imageBytes, MimeType = mimeType } }
                        }
                    }
                }
            },
            UsageMetadata = new GenerateContentResponseUsageMetadata
            {
                PromptTokenCount = promptTokens,
                CandidatesTokenCount = candidatesTokens
            }
        };

        private static GoogleImageEditService BuildService(ImageGenerationToolConfig config, Mock<IGoogleContentGenerationClient> client) =>
            new(config, client.Object);

        [Fact]
        public async Task EditImageAsync_HappyPath_ReturnsImageBytesAndTokenUsage()
        {
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(BuildResponse(new byte[] { 9, 9, 9 }, promptTokens: 100, candidatesTokens: 50));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation" }, client);

            var result = await service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(new byte[] { 9, 9, 9 }, result.ImageBytes);
            Assert.Equal("image/png", result.MimeType);
            Assert.Equal(100, result.TokenUsage.InputTokenCount);
            Assert.Equal(50, result.TokenUsage.OutputTokenCount);
        }

        [Fact]
        public async Task EditImageAsync_NoImageDataInResponse_Throws()
        {
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GenerateContentResponse { Candidates = new List<Candidate>() });

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation" }, client);

            await Assert.ThrowsAsync<ImageEditProviderException>(() =>
                service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken));
        }

        private static async Task<ImageEditProviderException> EditExpectingProviderFailure(GenerateContentResponse response)
        {
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation" }, client);

            return await Assert.ThrowsAsync<ImageEditProviderException>(() =>
                service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EditImageAsync_PromptBlockedForSafety_ThrowsSafetyBlocked()
        {
            var ex = await EditExpectingProviderFailure(new GenerateContentResponse
            {
                PromptFeedback = new GenerateContentResponsePromptFeedback { BlockReason = BlockedReason.Safety }
            });

            Assert.Equal(ImageEditFailureKind.SafetyBlocked, ex.Kind);
            Assert.NotNull(ex.BlockReason);
        }

        [Fact]
        public async Task EditImageAsync_FinishReasonImageSafety_ThrowsSafetyBlocked()
        {
            var ex = await EditExpectingProviderFailure(new GenerateContentResponse
            {
                Candidates = new List<Candidate> { new Candidate { FinishReason = FinishReason.ImageSafety } }
            });

            Assert.Equal(ImageEditFailureKind.SafetyBlocked, ex.Kind);
            Assert.Single(ex.FinishReasons);
        }

        [Fact]
        public async Task EditImageAsync_BlockedSafetyRating_ThrowsSafetyBlocked()
        {
            var ex = await EditExpectingProviderFailure(new GenerateContentResponse
            {
                Candidates = new List<Candidate>
                {
                    new Candidate { SafetyRatings = new List<SafetyRating> { new SafetyRating { Blocked = true } } }
                }
            });

            Assert.Equal(ImageEditFailureKind.SafetyBlocked, ex.Kind);
        }

        [Fact]
        public async Task EditImageAsync_TextOnlyResponse_ThrowsOther()
        {
            var ex = await EditExpectingProviderFailure(new GenerateContentResponse
            {
                Candidates = new List<Candidate>
                {
                    new Candidate
                    {
                        FinishReason = FinishReason.Stop,
                        Content = new Content { Parts = new List<Part> { new Part { Text = "I can't draw that." } } }
                    }
                }
            });

            Assert.Equal(ImageEditFailureKind.Other, ex.Kind);
        }

        [Fact]
        public async Task EditImageAsync_ClientThrowsApiException_PropagatesUnchanged()
        {
            var apiException = new Google.GenAI.ApiException("overloaded", 503, "UNAVAILABLE", null);
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(apiException);

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "m" }, client);

            var thrown = await Assert.ThrowsAsync<Google.GenAI.ApiException>(() =>
                service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken));

            Assert.Same(apiException, thrown);
        }

        [Fact]
        public async Task EditImageAsync_UrlOverload_Download403_ThrowsInputExceptionNotProviderError()
        {
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            httpTest.RespondWith("expired", 403);
            var client = new Mock<IGoogleContentGenerationClient>();
            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "m" }, client);

            var ex = await Assert.ThrowsAsync<ImageEditInputException>(() =>
                service.EditImageAsync("https://storage.example.com/expired.png", "test", cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("upload it again", ex.Message);
            Assert.NotNull(ex.InnerException);
            client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task EditImageAsync_UrlOverload_Download503_PropagatesFlurlExceptionForClassification()
        {
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            httpTest.RespondWith("down", 503);
            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "m" }, new Mock<IGoogleContentGenerationClient>());

            await Assert.ThrowsAsync<Flurl.Http.FlurlHttpException>(() =>
                service.EditImageAsync("https://storage.example.com/image.png", "test", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EditImageAsync_UrlOverload_DownloadTimeout_PropagatesTimeoutForClassification()
        {
            using var httpTest = new Flurl.Http.Testing.HttpTest();
            httpTest.SimulateTimeout();
            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "m" }, new Mock<IGoogleContentGenerationClient>());

            await Assert.ThrowsAsync<Flurl.Http.FlurlHttpTimeoutException>(() =>
                service.EditImageAsync("https://storage.example.com/image.png", "test", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EditImageAsync_UrlOverload_DownloadFailure_Throws()
        {
            var client = new Mock<IGoogleContentGenerationClient>();
            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation" }, client);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.EditImageAsync("https://nonexistent.invalid/image.png", "test", cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideProvided_SendsOverrideInsteadOfConfigDefault()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", Quality = "high" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", quality: "low", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(MediaResolution.MediaResolutionLow, captured.MediaResolution);
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideOmitted_FallsBackToConfigDefault()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", Quality = "medium" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(MediaResolution.MediaResolutionMedium, captured.MediaResolution);
        }

        [Fact]
        public async Task EditImageAsync_QualityOverrideIsEmptyString_FallsBackToConfigDefault()
        {
            // An LLM tool call can send "" instead of omitting an optional parameter; that must not
            // be treated as an explicit override, or it silently discards the tenant's configured quality.
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", Quality = "medium" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", quality: "", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(MediaResolution.MediaResolutionMedium, captured.MediaResolution);
        }

        [Fact]
        public async Task EditImageAsync_AspectRatioOverrideProvided_SendsOverrideInsteadOfConfigDefault()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", AspectRatio = "1:1" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", aspectRatio: "16:9", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("16:9", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_AspectRatioOverrideOmitted_FallsBackToConfigDefault()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", AspectRatio = "9:16" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("9:16", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_AspectRatioOverrideIsEmptyString_FallsBackToConfigDefault()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", AspectRatio = "9:16" }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", aspectRatio: "", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("9:16", captured.ImageConfig?.AspectRatio);
        }

        private static async Task<GenerateContentConfig> CaptureConfigAsync(ImageGenerationToolConfig config, byte[] input, string aspectRatio = null)
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, c, _) => captured = c)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            await BuildService(config, client).EditImageAsync(input, "make it blue", aspectRatio: aspectRatio, cancellationToken: TestContext.Current.CancellationToken);
            return captured;
        }

        private static ImageGenerationToolConfig GoogleConfig(string aspectRatio = null) =>
            new() { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", AspectRatio = aspectRatio };

        [Theory]
        [InlineData(3024, 4032, "3:4")]
        [InlineData(4032, 3024, "4:3")]
        [InlineData(1080, 1080, "1:1")]
        public async Task EditImageAsync_NoArgNoConfig_UsesDetectedInputRatio(int w, int h, string expected)
        {
            var captured = await CaptureConfigAsync(GoogleConfig(), TestImageBytes.Png(w, h));

            Assert.Equal(expected, captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_ConfigRatioAndPortraitInput_ConfigWins()
        {
            var captured = await CaptureConfigAsync(GoogleConfig("4:3"), TestImageBytes.Png(3024, 4032));

            Assert.Equal("4:3", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_ExifRotatedPhoneAndEmptyConfig_UsesDisplayedOrientation()
        {
            var captured = await CaptureConfigAsync(GoogleConfig(), TestImageBytes.Jpeg(4032, 3024, orientation: 6));

            Assert.Equal("3:4", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_ExplicitArgWinsOverDetected()
        {
            var captured = await CaptureConfigAsync(GoogleConfig("4:3"), TestImageBytes.Png(3024, 4032), aspectRatio: "16:9");

            Assert.Equal("16:9", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_UndetectableInput_FallsBackToConfig()
        {
            var captured = await CaptureConfigAsync(GoogleConfig("4:3"), FakeImageBytes);

            Assert.Equal("4:3", captured.ImageConfig?.AspectRatio);
        }

        [Fact]
        public async Task EditImageAsync_UndetectableInputAndNoConfig_DoesNotSetImageConfig()
        {
            var captured = await CaptureConfigAsync(GoogleConfig(), FakeImageBytes);

            Assert.Null(captured.ImageConfig);
        }

        [Fact]
        public async Task EditImageAsync_AspectRatioAndConfigBothEmpty_DoesNotSetImageConfig()
        {
            GenerateContentConfig captured = null;
            var client = new Mock<IGoogleContentGenerationClient>();
            client
                .Setup(x => x.GenerateContentAsync(It.IsAny<string>(), It.IsAny<Content>(), It.IsAny<GenerateContentConfig>(), It.IsAny<CancellationToken>()))
                .Callback<string, Content, GenerateContentConfig, CancellationToken>((_, _, config, _) => captured = config)
                .ReturnsAsync(BuildResponse(new byte[] { 1 }));

            var service = BuildService(new ImageGenerationToolConfig { Provider = "google", ApiKey = "fake-key", Model = "gemini-2.0-flash-preview-image-generation", AspectRatio = null }, client);

            await service.EditImageAsync(FakeImageBytes, "make it blue", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(captured.ImageConfig);
        }
    }
}
