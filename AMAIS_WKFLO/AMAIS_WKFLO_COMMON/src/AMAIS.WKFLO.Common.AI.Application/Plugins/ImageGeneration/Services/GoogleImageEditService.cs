using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Config;
using Flurl.Http;
using Google.GenAI;
using Google.GenAI.Types;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ImageGeneration.Services
{
    internal sealed class GoogleImageEditService : IImageEditService
    {
        private readonly ImageGenerationToolConfig _config;
        private readonly IGoogleContentGenerationClient _client;

        public GoogleImageEditService(ImageGenerationToolConfig config)
            : this(config, new GoogleGenAiContentGenerationClient(config.ApiKey))
        {
        }

        internal GoogleImageEditService(ImageGenerationToolConfig config, IGoogleContentGenerationClient client)
        {
            _config = config;
            _client = client;
        }

        public async Task<ImageEditResult> EditImageAsync(string imageUrl, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default)
        {
            var imageBytes = await ImageEditSourceDownloader.DownloadAsync(imageUrl, cancellationToken);
            return await EditImageAsync(imageBytes, instruction, quality, aspectRatio, cancellationToken);
        }

        public async Task<ImageEditResult> EditImageAsync(byte[] imageBytes, string instruction, string quality = null, string aspectRatio = null, CancellationToken cancellationToken = default)
        {
            var modelName = _config.Model;
            var (mimeType, extension) = ImageMimeTypeHelper.DetectFromBytes(imageBytes);

            var content = new Content
            {
                Parts = new List<Part>
                {
                    new Part { Text = instruction },
                    new Part { InlineData = new Blob { Data = imageBytes, MimeType = mimeType } }
                },
                Role = "user"
            };

            var genConfig = new GenerateContentConfig
            {
                ResponseModalities = new List<string> { "IMAGE", "TEXT" },
                MediaResolution = MapMediaResolution(string.IsNullOrWhiteSpace(quality) ? _config.Quality : quality)
            };

            // Precedence: explicit LLM argument > tool config (a fixed admin choice) > ratio detected from the input
            // image (empty config means "Auto, match input") > none.
            var effectiveAspectRatio = !string.IsNullOrWhiteSpace(aspectRatio) ? aspectRatio
                : !string.IsNullOrWhiteSpace(_config.AspectRatio) ? _config.AspectRatio
                : ImageDimensionsHelper.TryGetDimensions(imageBytes, out var inputWidth, out var inputHeight)
                    ? AspectRatioMapper.ToNearestGoogleAspectRatio(inputWidth, inputHeight)
                    : null;
            if (!string.IsNullOrWhiteSpace(effectiveAspectRatio))
                genConfig.ImageConfig = new ImageConfig { AspectRatio = effectiveAspectRatio };

            var response = await _client.GenerateContentAsync(modelName, content, genConfig, cancellationToken);

            var imagePart = response.Parts?.FirstOrDefault(p => p.InlineData?.Data?.Length > 0)
                ?? throw BuildNoImageException(modelName, response);

            return new ImageEditResult(imagePart.InlineData!.Data!, mimeType, extension, new AITokenUsage()
            {
                InputTokenCount = response.UsageMetadata?.PromptTokenCount ?? 0,
                OutputTokenCount = response.UsageMetadata?.CandidatesTokenCount ?? 0
            });
        }

        private static readonly HashSet<string> SafetyFinishReasons = new(StringComparer.OrdinalIgnoreCase)
        {
            "SAFETY", "IMAGE_SAFETY", "PROHIBITED_CONTENT", "IMAGE_PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION", "IMAGE_RECITATION"
        };

        // US 7564: a 200 response without an image is a safety block when Google says so (prompt block reason, a
        // safety-type finish reason or a blocked safety rating); anything else stays "Other".
        private static ImageEditProviderException BuildNoImageException(string modelName, GenerateContentResponse response)
        {
            var blockReason = response?.PromptFeedback?.BlockReason?.ToString();
            if (string.IsNullOrWhiteSpace(blockReason) || IsUnspecified(blockReason))
                blockReason = null;

            var finishReasons = response?.Candidates?
                .Where(c => c.FinishReason != null)
                .Select(c => c.FinishReason!.ToString())
                .ToList() ?? new List<string>();

            var ratingBlocked = response?.Candidates?.Any(c => c.SafetyRatings?.Any(r => r.Blocked == true) == true) == true;

            var safety = blockReason != null
                || finishReasons.Any(r => SafetyFinishReasons.Contains(r))
                || ratingBlocked;

            return new ImageEditProviderException(
                safety ? ImageEditFailureKind.SafetyBlocked : ImageEditFailureKind.Other,
                "google",
                BuildNoImageErrorMessage(modelName, response),
                blockReason,
                finishReasons);
        }

        private static bool IsUnspecified(string reason) =>
            reason.Replace("_", string.Empty).Equals("BlockedReasonUnspecified", StringComparison.OrdinalIgnoreCase);

        private static string BuildNoImageErrorMessage(string modelName, GenerateContentResponse response)
        {
            var details = new List<string>();

            var feedback = response?.PromptFeedback;
            if (feedback?.BlockReason != null)
                details.Add($"PromptBlockReason={feedback.BlockReason}");
            if (!string.IsNullOrWhiteSpace(feedback?.BlockReasonMessage))
                details.Add($"PromptBlockMessage='{feedback.BlockReasonMessage}'");

            var candidates = response?.Candidates;
            if (candidates == null || candidates.Count == 0)
            {
                details.Add("Candidates=0");
            }
            else
            {
                details.Add($"Candidates={candidates.Count}");
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    var candidateDetails = new List<string>();

                    if (candidate.FinishReason != null)
                        candidateDetails.Add($"FinishReason={candidate.FinishReason}");
                    if (!string.IsNullOrWhiteSpace(candidate.FinishMessage))
                        candidateDetails.Add($"FinishMessage='{candidate.FinishMessage}'");

                    var blockedRatings = candidate.SafetyRatings?
                        .Where(r => r.Blocked == true)
                        .Select(r => $"{r.Category}:{r.Probability}")
                        .ToList();
                    if (blockedRatings?.Count > 0)
                        candidateDetails.Add($"BlockedSafety=[{string.Join(", ", blockedRatings)}]");

                    var text = string.Join(" ", candidate.Content?.Parts?
                        .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                        .Select(p => p.Text.Trim()) ?? Enumerable.Empty<string>());
                    if (!string.IsNullOrWhiteSpace(text))
                        candidateDetails.Add($"Text='{(text.Length > 500 ? text[..500] + "..." : text)}'");

                    details.Add($"Candidate[{i}]: {(candidateDetails.Count > 0 ? string.Join(", ", candidateDetails) : "no details")}");
                }
            }

            return $"Google model '{modelName}' returned no image data. {string.Join("; ", details)}";
        }

        private static MediaResolution MapMediaResolution(string quality) => quality?.ToLowerInvariant() switch
        {
            "low" => MediaResolution.MediaResolutionLow,
            "medium" => MediaResolution.MediaResolutionMedium,
            _ => MediaResolution.MediaResolutionHigh
        };

        private sealed class GoogleGenAiContentGenerationClient : IGoogleContentGenerationClient
        {
            private readonly Client _client;

            public GoogleGenAiContentGenerationClient(string apiKey)
            {
                _client = new Client(apiKey: apiKey);
            }

            public Task<GenerateContentResponse> GenerateContentAsync(string model, Content contents, GenerateContentConfig config, CancellationToken cancellationToken)
                => _client.Models.GenerateContentAsync(model, contents, config, cancellationToken);
        }
    }
}
