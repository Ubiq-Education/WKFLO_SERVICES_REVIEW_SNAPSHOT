using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Config;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    internal sealed class AzureGptImageEditService : IImageEditService
    {
        private readonly ImageGenerationToolConfig _config;

        public AzureGptImageEditService(ImageGenerationToolConfig config)
        {
            _config = config;
        }

        public async Task<ImageEditResult> EditImageAsync(string imageUrl, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default)
        {
            var imageBytes = await ImageEditSourceDownloader.DownloadAsync(imageUrl, cancellationToken);
            return await EditImageAsync(imageBytes, instruction, quality, aspectRatio, cancellationToken);
        }

        public async Task<ImageEditResult> EditImageAsync(byte[] imageBytes, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default)
        {
            var deploymentName = !string.IsNullOrWhiteSpace(_config.DeploymentName) ? _config.DeploymentName
                : !string.IsNullOrWhiteSpace(_config.Model) ? _config.Model
                : throw new ImageEditProviderException(ImageEditFailureKind.NotConfigured, "azure", "Azure image edit service requires 'deploymentName' (or 'model') in the tool configuration.");

            var endpoint = !string.IsNullOrWhiteSpace(_config.Endpoint) ? _config.Endpoint.TrimEnd('/')
                : throw new ImageEditProviderException(ImageEditFailureKind.NotConfigured, "azure", "Azure image edit service requires 'endpoint' in the tool configuration.");

            var apiVersion = string.IsNullOrWhiteSpace(_config.ApiVersion) ? AzureImageApiVersion.Default : _config.ApiVersion;
            var url = $"{endpoint}/openai/deployments/{deploymentName}/images/edits?api-version={apiVersion}";

            // images/edits accepts `size` (auto, 1024x1024, 1536x1024, 1024x1536 for gpt-image-1). There is no free
            // aspect ratio, so the closest supported size is sent. Precedence: explicit LLM argument > tool config
            // (a fixed admin choice) > ratio detected from the input image (empty config means "Auto, match input")
            // > omit (provider default).
            var size = !string.IsNullOrWhiteSpace(aspectRatio) ? AspectRatioMapper.ToNearestAzureSize(aspectRatio) : null;
            size ??= AspectRatioMapper.ToNearestAzureSize(_config.AspectRatio);
            if (size == null && ImageDimensionsHelper.TryGetDimensions(imageBytes, out var inputWidth, out var inputHeight))
                size = AspectRatioMapper.ToNearestAzureSize(inputWidth, inputHeight);

            // `size` is only documented for 2025-04-01-preview and later; other versions keep the provider default.
            if (!AzureImageApiVersion.SupportsEditSize(apiVersion))
                size = null;

            using var imageStream = new MemoryStream(imageBytes);
            var response = await url
                .WithHeader("api-key", _config.ApiKey)
                .PostMultipartAsync(
                    mp =>
                    {
                        mp.AddFile("image[]", imageStream, "input.png", "image/png");
                        mp.AddString("prompt", instruction);
                        mp.AddString("model", deploymentName);
                        mp.AddString("quality", MapQuality(string.IsNullOrWhiteSpace(quality) ? _config.Quality : quality));
                        mp.AddString("response_format", "b64_json");
                        if (size != null)
                            mp.AddString("size", size);
                    },
                    cancellationToken: cancellationToken);

            var json = await response.GetStringAsync();
            var result = JsonConvert.DeserializeObject<AzureImageEditApiResponse>(json)
                ?? throw new InvalidOperationException("Azure image edit API returned an empty response.");

            return new ImageEditResult(Convert.FromBase64String(result.Data[0].B64Json), "image/png", "png", new AITokenUsage());
        }

        private static string MapQuality(string quality) => quality?.ToLowerInvariant() switch
        {
            "low" => "low",
            "medium" => "medium",
            _ => "high"
        };

        private sealed class AzureImageEditApiResponse
        {
            [JsonProperty("data")]
            public List<AzureImageEditItem> Data { get; set; }
        }

        private sealed class AzureImageEditItem
        {
            [JsonProperty("b64_json")]
            public string B64Json { get; set; }
        }
    }
}
