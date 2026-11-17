using AMAIS.WKFLO.Common.AI.Application.Helpers;

namespace AMAIS.WKFLO.AI.UnitTests.Helpers
{
    public class ImageMimeTypeHelperTests
    {
        // PNG: 89 50 4E 47
        [Fact]
        public void DetectFromBytes_PngMagicBytes_ReturnsPng()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.Equal(("image/png", "png"), ImageMimeTypeHelper.DetectFromBytes(png));
        }

        // JPEG: FF D8 FF
        [Fact]
        public void DetectFromBytes_JpegMagicBytes_ReturnsJpeg()
        {
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
            Assert.Equal(("image/jpeg", "jpeg"), ImageMimeTypeHelper.DetectFromBytes(jpeg));
        }

        // WebP: RIFF....WEBP at offsets 0 and 8
        [Fact]
        public void DetectFromBytes_WebPMagicBytes_ReturnsWebP()
        {
            var webp = new byte[12];
            webp[0] = 0x52; webp[1] = 0x49; webp[2] = 0x46; webp[3] = 0x46; // RIFF
            webp[8] = 0x57; webp[9] = 0x45; webp[10] = 0x42; webp[11] = 0x50; // WEBP
            Assert.Equal(("image/webp", "webp"), ImageMimeTypeHelper.DetectFromBytes(webp));
        }

        // WEBP marker present at offset 8-11 but no RIFF header at offset 0-3: must not be misdetected as WebP
        [Fact]
        public void DetectFromBytes_WebPMarkerWithoutRiffHeader_DefaultsToPng()
        {
            var notWebp = new byte[12];
            notWebp[0] = 0x00; notWebp[1] = 0x00; notWebp[2] = 0x00; notWebp[3] = 0x00; // not RIFF
            notWebp[8] = 0x57; notWebp[9] = 0x45; notWebp[10] = 0x42; notWebp[11] = 0x50; // WEBP
            Assert.Equal(("image/png", "png"), ImageMimeTypeHelper.DetectFromBytes(notWebp));
        }

        // GIF: 47 49 46
        [Fact]
        public void DetectFromBytes_GifMagicBytes_ReturnsGif()
        {
            var gif = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
            Assert.Equal(("image/gif", "gif"), ImageMimeTypeHelper.DetectFromBytes(gif));
        }

        // Unknown format defaults to image/png
        [Fact]
        public void DetectFromBytes_UnknownFormat_DefaultsToPng()
        {
            var unknown = new byte[] { 0x00, 0x01, 0x02, 0x03 };
            Assert.Equal(("image/png", "png"), ImageMimeTypeHelper.DetectFromBytes(unknown));
        }

        // Edge case: very short array still returns default
        [Fact]
        public void DetectFromBytes_EmptyArray_DefaultsToPng()
        {
            Assert.Equal(("image/png", "png"), ImageMimeTypeHelper.DetectFromBytes(System.Array.Empty<byte>()));
        }

        // PNG header wins over JPEG when first bytes match PNG
        [Fact]
        public void DetectFromBytes_PngBytesWithJpegTrailer_ReturnsPng()
        {
            var mixed = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0xD8, 0xFF };
            Assert.Equal(("image/png", "png"), ImageMimeTypeHelper.DetectFromBytes(mixed));
        }
    }
}
