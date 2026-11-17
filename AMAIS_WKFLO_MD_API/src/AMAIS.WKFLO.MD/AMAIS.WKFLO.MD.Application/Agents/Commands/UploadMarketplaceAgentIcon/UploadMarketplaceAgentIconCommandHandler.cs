using AMAIS.WKFLO.Common.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UploadMarketplaceAgentIcon
{
    // Mirrors UploadAgentChatIconCommandHandler's image validate/resize/upload logic exactly, but
    // targets MarketplaceAgent instead of TenantAgent, and does not touch AgentChatSetting at all —
    // a MarketplaceAgent has no chat settings, its icon lives directly on
    // MarketplaceAgent.IconUrl (set by the caller's own MarketplaceAgentUpdate call, same "upload
    // returns a URL, caller persists it" separation).
    public class UploadMarketplaceAgentIconCommandHandler : IRequestHandler<UploadMarketplaceAgentIconCommand, Result>
    {
        private const string ContainerName = "$web";
        private const int MaxDimension = 1024;
        private const int EncodeQuality = 90; // Ignored by the (lossless) PNG encoder.

        // Only PNG/JPEG/WEBP are accepted — no SVG (script-injection risk), no GIF (no use case).
        private static readonly Dictionary<SKEncodedImageFormat, (string Extension, string ContentType)> AllowedFormats = new()
        {
            [SKEncodedImageFormat.Png] = (".png", "image/png"),
            [SKEncodedImageFormat.Jpeg] = (".jpg", "image/jpeg"),
            [SKEncodedImageFormat.Webp] = (".webp", "image/webp")
        };

        private readonly IMasterDataDbContext _db;
        private readonly IImageStorageService _imageStorageService;
        private readonly ILogger<UploadMarketplaceAgentIconCommandHandler> _logger;

        public UploadMarketplaceAgentIconCommandHandler(
            IMasterDataDbContext db,
            IImageStorageService imageStorageService,
            ILogger<UploadMarketplaceAgentIconCommandHandler> logger)
        {
            _db = db;
            _imageStorageService = imageStorageService;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(UploadMarketplaceAgentIconCommand command, CancellationToken cancellationToken)
        {
            // Marketplace agents are a global catalog, not tenant-scoped data (see
            // BaseMasterDataValidator.BeExistingMarketplaceAgent's doc comment) — this endpoint is
            // AMAIS-staff "Manage Marketplace Agents" tooling that must be able to upload an icon
            // for any marketplace agent regardless of the caller's current tenant context. No
            // tenant filter here, deliberately — same fix as UpdateMarketplaceAgentCommandValidator/
            // DeleteMarketplaceAgentCommandValidator (WI 7312).
            var marketplaceAgent = await _db.MarketplaceAgents
                .AsNoTracking()
                .Where(a => a.Id == command.Id && !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (marketplaceAgent == null)
            {
                return new InvalidResult(nameof(MarketplaceAgent), ErrorConst.AgentNotFound);
            }

            SKCodec codec;
            try
            {
                command.Content.Position = 0;
                codec = SKCodec.Create(command.Content);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unable to decode uploaded icon for marketplace agent {AgentId}", command.Id);
                return new InvalidResult(nameof(command.Content), ErrorConst.ImageDecodeFailed);
            }

            if (codec == null)
            {
                return new InvalidResult(nameof(command.Content), ErrorConst.ImageDecodeFailed);
            }

            using (codec)
            {
                if (!AllowedFormats.TryGetValue(codec.EncodedFormat, out var format))
                {
                    return new InvalidResult(nameof(command.ContentType), ErrorConst.ImageFormatInvalid);
                }

                using var bitmap = SKBitmap.Decode(codec);
                if (bitmap == null)
                {
                    _logger.LogError("SKBitmap.Decode returned null for uploaded icon for marketplace agent {AgentId}", command.Id);
                    return new InvalidResult(nameof(command.Content), ErrorConst.ImageDecodeFailed);
                }

                var resizedBitmap = bitmap;
                SKBitmap scaledBitmap = null;
                try
                {
                    if (bitmap.Width > MaxDimension || bitmap.Height > MaxDimension)
                    {
                        var scale = Math.Min((double)MaxDimension / bitmap.Width, (double)MaxDimension / bitmap.Height);
                        var newWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                        var newHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

                        scaledBitmap = bitmap.Resize(new SKSizeI(newWidth, newHeight), SKSamplingOptions.Default);
                        if (scaledBitmap == null)
                        {
                            _logger.LogError("SKBitmap.Resize returned null for uploaded icon for marketplace agent {AgentId}", command.Id);
                            return new InvalidResult(nameof(command.Content), ErrorConst.ImageDecodeFailed);
                        }

                        resizedBitmap = scaledBitmap;
                    }

                    using var image = SKImage.FromBitmap(resizedBitmap);
                    using var encoded = image.Encode(codec.EncodedFormat, EncodeQuality);

                    using var outputStream = new MemoryStream();
                    encoded.SaveTo(outputStream);
                    outputStream.Position = 0;

                    // Fully anonymized path — no original filename retained. Prefixed "marketplace-agent"
                    // (rather than the tenant name UploadAgentChatIconCommandHandler uses) since a
                    // Marketplace agent's icon is a public catalog asset, not tenant-private chat config.
                    var relativePath = $"marketplace-agent/{command.Id}/{Guid.NewGuid()}{format.Extension}";

                    var uploadResult = await _imageStorageService.SaveImageAsync(
                        ContainerName,
                        relativePath,
                        outputStream,
                        format.ContentType,
                        cancellationToken);

                    return new SuccessResult<MarketplaceAgentIconUploadResult>(new MarketplaceAgentIconUploadResult
                    {
                        RelativePath = uploadResult.RelativePath,
                        AbsoluteUrl = uploadResult.AbsoluteUrl
                    });
                }
                finally
                {
                    scaledBitmap?.Dispose();
                }
            }
        }
    }
}
