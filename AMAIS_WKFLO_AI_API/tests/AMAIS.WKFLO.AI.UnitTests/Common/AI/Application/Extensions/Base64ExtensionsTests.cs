using AMAIS.WKFLO.Common.AI.Application.Extensions;
using System;
using System.Linq;
using System.Text;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Extensions
{
    public class Base64ExtensionsTests
    {
        private static string ToDataUri(string declaredMimeType, byte[] bytes)
            => $"data:{declaredMimeType};base64,{Convert.ToBase64String(bytes)}";

        private static byte[] PngBytes(int paddingLength = 16) =>
            [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. new byte[paddingLength]];

        private static byte[] JpegBytes(int paddingLength = 16) =>
            [.. new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, .. new byte[paddingLength]];

        private static byte[] GifBytes(int paddingLength = 16) =>
            [.. Encoding.ASCII.GetBytes("GIF89a"), .. new byte[paddingLength]];

        private static byte[] WebPBytes(int paddingLength = 16) =>
            [.. Encoding.ASCII.GetBytes("RIFF"), .. new byte[4], .. Encoding.ASCII.GetBytes("WEBP"), .. new byte[paddingLength]];

        private static byte[] BmpBytes(int paddingLength = 16) =>
            [.. new byte[] { 0x42, 0x4D }, .. new byte[paddingLength]];

        private static byte[] TiffBytes(int paddingLength = 16) =>
            [.. new byte[] { 0x49, 0x49, 0x2A, 0x00 }, .. new byte[paddingLength]];

        private static byte[] HeicBytes(string brand = "heic", int paddingLength = 16) =>
            [.. new byte[] { 0x00, 0x00, 0x00, 0x18 }, .. Encoding.ASCII.GetBytes("ftyp"), .. Encoding.ASCII.GetBytes(brand), .. new byte[paddingLength]];

        // "WEBP" fourCC at offset 8 without the "RIFF" header at offset 0 — must not false-positive
        // as WebP (the magic-number check requires both markers, not just the fourCC).
        private static byte[] WebPFourCcWithoutRiffHeader(int paddingLength = 16) =>
            [.. new byte[4], .. new byte[4], .. Encoding.ASCII.GetBytes("WEBP"), .. new byte[paddingLength]];

        [Fact]
        public void ParseImageData_HeaderMismatchesActualBytes_SelfCorrectsToSniffedType()
        {
            // Header claims PNG, bytes are actually JPEG — the exact case that made Anthropic reject
            // the request (story #6670): "image was specified using image/png ... appears to be image/jpeg".
            var dataUri = ToDataUri("image/png", JpegBytes());

            var (_, mimeType) = dataUri.ParseImageData();

            Assert.Equal("image/jpeg", mimeType);
        }

        [Fact]
        public void ParseImageData_HeaderMatchesActualBytes_ReturnsUnchanged()
        {
            var dataUri = ToDataUri("image/png", PngBytes());

            var (_, mimeType) = dataUri.ParseImageData();

            Assert.Equal("image/png", mimeType);
        }

        [Fact]
        public void ParseImageData_UnrecognizedDeclaredFormat_KeepsHeaderUnchanged()
        {
            // "image/svg+xml" isn't one of the sniffed signatures — bytes here don't match any known
            // magic number either, so the declared header must be trusted as-is (no false override).
            var dataUri = ToDataUri("image/svg+xml", Encoding.UTF8.GetBytes("<svg></svg>"));

            var (_, mimeType) = dataUri.ParseImageData();

            Assert.Equal("image/svg+xml", mimeType);
        }

        [Theory]
        [InlineData("image/heic", true)]
        [InlineData("image/bmp", true)]
        [InlineData("image/tiff", true)]
        [InlineData("image/png", false)]
        public void IsUnsupportedImageFormat_DetectsFormatsNoProviderCanUse(string declaredMimeType, bool expectedUnsupported)
        {
            byte[] bytes = declaredMimeType switch
            {
                "image/heic" => HeicBytes(),
                "image/bmp" => BmpBytes(),
                "image/tiff" => TiffBytes(),
                _ => PngBytes()
            };
            var dataUri = ToDataUri(declaredMimeType, bytes);

            Assert.Equal(expectedUnsupported, dataUri.IsUnsupportedImageFormat());
        }

        [Fact]
        public void IsUnsupportedImageFormat_RecognizedSupportedFormats_AreNotFlagged()
        {
            Assert.False(ToDataUri("image/png", PngBytes()).IsUnsupportedImageFormat());
            Assert.False(ToDataUri("image/jpeg", JpegBytes()).IsUnsupportedImageFormat());
            Assert.False(ToDataUri("image/gif", GifBytes()).IsUnsupportedImageFormat());
            Assert.False(ToDataUri("image/webp", WebPBytes()).IsUnsupportedImageFormat());
        }

        [Fact]
        public void IsUnsupportedImageFormat_UnrecognizedBytes_AreNotFlagged()
        {
            // Bytes matching no known signature at all — must not be flagged as "unsupported" since
            // we can't actually tell what it is.
            var dataUri = ToDataUri("image/x-made-up", new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

            Assert.False(dataUri.IsUnsupportedImageFormat());
        }

        [Fact]
        public void IsUnsupportedImageFormat_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(((string)null).IsUnsupportedImageFormat());
            Assert.False(string.Empty.IsUnsupportedImageFormat());
        }

        [Theory]
        [InlineData("heic")]
        [InlineData("heix")]
        [InlineData("mif1")]
        [InlineData("hevc")]
        public void IsUnsupportedImageFormat_RecognizesMultipleHeicBrandVariants(string brand)
        {
            var dataUri = ToDataUri("image/heic", HeicBytes(brand));

            Assert.True(dataUri.IsUnsupportedImageFormat());
        }

        [Fact]
        public void ParseImageData_WebPFourCcWithoutRiffHeader_IsNotMisdetectedAsWebP()
        {
            // Bytes that happen to contain "WEBP" at offset 8 but lack the "RIFF" header must not be
            // sniffed as WebP — the declared (mismatched-but-unrecognized) header should be kept as-is.
            var dataUri = ToDataUri("image/x-unknown", WebPFourCcWithoutRiffHeader());

            var (_, mimeType) = dataUri.ParseImageData();

            Assert.Equal("image/x-unknown", mimeType);
        }

        // Bug 8189: data URIs are decoded straight from the string (no Substring copy); the bytes and the
        // FormatException for bad input must match Convert.FromBase64String exactly.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(1000)]
        public void ParseDocumentData_DataUri_DecodesSameBytesAsFromBase64String(int length)
        {
            var bytes = new byte[length];
            new Random(length).NextBytes(bytes);

            var (decoded, mimeType, _) = ToDataUri("application/octet-stream", bytes).ParseDocumentData();

            Assert.Equal(bytes, decoded);
            Assert.Equal("application/octet-stream", mimeType);
        }

        [Fact]
        public void ParseImageData_DataUriWithWhitespace_DecodesLikeFromBase64String()
        {
            var base64 = Convert.ToBase64String(JpegBytes(200));
            var wrapped = string.Join("\r\n", base64.Chunk(76).Select(c => new string(c)));

            var (decoded, _) = $"data:image/jpeg;base64,{wrapped}".ParseImageData();

            Assert.Equal(Convert.FromBase64String(wrapped), decoded);
        }

        [Fact]
        public void ParseDocumentData_InvalidBase64_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => "data:application/pdf;base64,not*base64".ParseDocumentData());
        }

        [Fact]
        public void ParseImageData_LargeDataUri_DoesNotCopyThePayload()
        {
            var image = JpegBytes(3_000_000);
            var dataUri = ToDataUri("image/jpeg", image);
            _ = dataUri.ParseImageData(); // warm up

            var before = GC.GetAllocatedBytesForCurrentThread();
            var (decoded, _) = dataUri.ParseImageData();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The decoded bytes only (~3 MB). The old Substring copy added 8 MB (4M base64 chars as UTF-16).
            Assert.Equal(image.Length, decoded.Length);
            Assert.True(allocated < image.Length * 1.5, $"allocated {allocated:N0} bytes");
        }
    }
}
