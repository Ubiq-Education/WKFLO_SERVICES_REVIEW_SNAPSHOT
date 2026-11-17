using AMAIS.WKFLO.Common.Contexts;
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

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UploadAgentChatIcon
{
    public class UploadAgentChatIconCommandHandler : IRequestHandler<UploadAgentChatIconCommand, Result>
    {
        // Public container ("$web" static website root) the resulting AbsoluteUrl resolves against
        // via ImageStorageService's STATIC_WEBSITE_BASE_URL env var.
        private const string ContainerName = "$web";
        private const int MaxDimension = 1024;
        private const int EncodeQuality = 90; // Ignored by the (lossless) PNG encoder.

        // Only PNG/JPEG/WEBP are accepted — no SVG (script-injection risk), no GIF (no use case).
        // Keyed by SkiaSharp's SKEncodedImageFormat (the format SKCodec actually detected).
        private static readonly Dictionary<SKEncodedImageFormat, (string Extension, string ContentType)> AllowedFormats = new()
        {
            [SKEncodedImageFormat.Png] = (".png", "image/png"),
            [SKEncodedImageFormat.Jpeg] = (".jpg", "image/jpeg"),
            [SKEncodedImageFormat.Webp] = (".webp", "image/webp")
        };

        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;
        private readonly IImageStorageService _imageStorageService;
        private readonly ILogger<UploadAgentChatIconCommandHandler> _logger;

        public UploadAgentChatIconCommandHandler(
            IMasterDataDbContext db,
            TenantContext tenantContext,
            IImageStorageService imageStorageService,
            ILogger<UploadAgentChatIconCommandHandler> logger)
        {
            _db = db;
            _tenantContext = tenantContext;
            _imageStorageService = imageStorageService;
            _logger = logger;
        }

        public async ValueTask<Result> Handle(UploadAgentChatIconCommand command, CancellationToken cancellationToken)
        {
            // Verify agent exists and belongs to tenant — same check UpdateAgentChatSettingCommandHandler
            // performs at handler level for this same entity.
            var tenantAgent = await _db.TenantAgents
                .AsNoTracking()
                .Where(a => a.Id == command.Id && a.Tenant.Name == _tenantContext.TenantName && !a.IsDeleted)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantAgent == null)
            {
                return new InvalidResult(nameof(TenantAgent), ErrorConst.AgentNotFound);
            }

            SKCodec codec;
            try
            {
                command.Content.Position = 0;
                codec = SKCodec.Create(command.Content);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unable to decode uploaded chat icon for agent {AgentId}", command.Id);
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
                    _logger.LogError("SKBitmap.Decode returned null for uploaded chat icon for agent {AgentId}", command.Id);
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
                            _logger.LogError("SKBitmap.Resize returned null for uploaded chat icon for agent {AgentId}", command.Id);
                            return new InvalidResult(nameof(command.Content), ErrorConst.ImageDecodeFailed);
                        }

                        resizedBitmap = scaledBitmap;
                    }

                    using var image = SKImage.FromBitmap(resizedBitmap);
                    using var encoded = image.Encode(codec.EncodedFormat, EncodeQuality);

                    using var outputStream = new MemoryStream();
                    encoded.SaveTo(outputStream);
                    outputStream.Position = 0;

                    // Fully anonymized path — no original filename retained.
                    var relativePath = $"{_tenantContext.TenantName}/{command.Id}/{Guid.NewGuid()}{format.Extension}";

                    var uploadResult = await _imageStorageService.SaveImageAsync(
                        ContainerName,
                        relativePath,
                        outputStream,
                        format.ContentType,
                        cancellationToken);

                    return new SuccessResult<AgentChatIconUploadResult>(new AgentChatIconUploadResult
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
