using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UploadMarketplaceAgentIcon;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UploadMarketplaceAgentIcon
{
    public class UploadMarketplaceAgentIconCommandHandlerTests : IClassFixture<UploadMarketplaceAgentIconCommandHandlerTesting>
    {
        // Classic minimal 1x1 transparent GIF87a spacer image — a real, decodable GIF used purely
        // as "valid image, disallowed format" test data (SkiaSharp's default encoders don't support
        // encoding GIF, only decoding it, so this well-known fixture is used instead of generating
        // one at runtime).
        private const string OnePixelGifBase64 = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBTAA7";

        private readonly UploadMarketplaceAgentIconCommandHandlerTesting _testing;

        public UploadMarketplaceAgentIconCommandHandlerTests(UploadMarketplaceAgentIconCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        private static MemoryStream EncodeImage(int width, int height, SKEncodedImageFormat format)
        {
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 90);

            var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;
            return stream;
        }

        [Fact]
        public async Task Handle_AgentDoesNotExist_ReturnsInvalidResultWithAgentNotFound()
        {
            // Arrange
            using var content = EncodeImage(10, 10, SKEncodedImageFormat.Png);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = 999,
                Content = content,
                FileName = "icon.png",
                ContentType = "image/png"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.AgentNotFound && e.Property == nameof(MarketplaceAgent));
        }

        [Fact]
        public async Task Handle_AgentSharedByAnotherTenant_StillSucceeds_MarketplaceIsNotTenantScoped()
        {
            // Arrange — WI 7312 fix: Marketplace-scope operations (this endpoint lives under
            // EndPoints/Marketplace/Agents/, the internal AMAIS-staff "Manage Marketplace Agents"
            // tool) must be able to manage every marketplace agent across every tenant, not just
            // whichever tenant happens to be in the caller's current TenantContext. Proves a
            // marketplace agent shared by "Other" tenant can still have its icon uploaded
            // successfully (previously this incorrectly 400'd with AgentNotFound).
            using var content = EncodeImage(10, 10, SKEncodedImageFormat.Png);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.OtherTenantAgentId,
                Content = content,
                FileName = "icon.png",
                ContentType = "image/png"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentIconUploadResult>>(result);
        }

        [Fact]
        public async Task Handle_AgentIsSoftDeleted_ReturnsInvalidResultWithAgentNotFound()
        {
            // Arrange
            using var content = EncodeImage(10, 10, SKEncodedImageFormat.Png);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.DeletedAgentId,
                Content = content,
                FileName = "icon.png",
                ContentType = "image/png"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.AgentNotFound);
        }

        [Fact]
        public async Task Handle_NonDecodableFile_ReturnsInvalidResultWithImageDecodeFailed()
        {
            // Arrange — garbage bytes, not a valid image of any format.
            using var content = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.ExistingAgentId,
                Content = content,
                FileName = "icon.png",
                ContentType = "image/png"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ImageDecodeFailed);
        }

        [Fact]
        public async Task Handle_ValidButDisallowedFormat_ReturnsInvalidResultWithImageFormatInvalid()
        {
            // Arrange — a real, decodable GIF, which is explicitly disallowed by this endpoint.
            using var content = new MemoryStream(Convert.FromBase64String(OnePixelGifBase64));
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.ExistingAgentId,
                Content = content,
                FileName = "icon.gif",
                ContentType = "image/gif"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            var invalid = Assert.IsType<InvalidResult>(result);
            Assert.Contains(invalid.Errors, e => e.Code == ErrorConst.ImageFormatInvalid);
        }

        [Fact]
        public async Task Handle_OversizedImage_IsResizedToMax1024_PreservingAspectRatio()
        {
            // Arrange — 2000x500 (4:1 aspect), both dimensions exceed the 1024px cap on width.
            using var content = EncodeImage(2000, 500, SKEncodedImageFormat.Jpeg);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.ExistingAgentId,
                Content = content,
                FileName = "icon.jpg",
                ContentType = "image/jpeg"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentIconUploadResult>>(result);

            // The fixture's upload-capture lists are shared across all tests in this class (via
            // IClassFixture), so take the most recent capture rather than asserting a single total.
            Assert.NotEmpty(_testing.UploadedContents);
            var uploadedBytes = _testing.UploadedContents[^1];
            using var uploadedBitmap = SKBitmap.Decode(uploadedBytes);
            Assert.NotNull(uploadedBitmap);
            Assert.True(uploadedBitmap.Width <= 1024);
            Assert.True(uploadedBitmap.Height <= 1024);
            // Aspect ratio preserved (4:1)
            Assert.Equal(4, uploadedBitmap.Width / uploadedBitmap.Height);
        }

        [Fact]
        public async Task Handle_ValidSmallPngImage_HappyPath_ReturnsAnonymizedAgentScopedPathAndAbsoluteUrl()
        {
            // Arrange — small enough that no resize is needed.
            using var content = EncodeImage(100, 50, SKEncodedImageFormat.Png);
            var command = new UploadMarketplaceAgentIconCommand
            {
                Id = UploadMarketplaceAgentIconCommandHandlerTesting.ExistingAgentId,
                Content = content,
                FileName = "my-original-filename.png",
                ContentType = "image/png"
            };

            // Act
            var result = await _testing.Handler.Handle(command, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentIconUploadResult>>(result);
            Assert.NotNull(success.Data);
            Assert.StartsWith($"marketplace-agent/{UploadMarketplaceAgentIconCommandHandlerTesting.ExistingAgentId}/", success.Data.RelativePath);
            Assert.EndsWith(".png", success.Data.RelativePath);
            Assert.DoesNotContain("my-original-filename", success.Data.RelativePath);
            Assert.Equal($"https://staging.static.wkflo.ai/{success.Data.RelativePath}", success.Data.AbsoluteUrl);
        }
    }
}
