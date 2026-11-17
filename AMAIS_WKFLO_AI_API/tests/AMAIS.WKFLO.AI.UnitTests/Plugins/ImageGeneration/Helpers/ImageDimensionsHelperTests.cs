using AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Helpers
{
    public class ImageDimensionsHelperTests
    {
        private static void AssertDims(byte[] bytes, int expectedWidth, int expectedHeight)
        {
            Assert.True(ImageDimensionsHelper.TryGetDimensions(bytes, out var w, out var h));
            Assert.Equal(expectedWidth, w);
            Assert.Equal(expectedHeight, h);
        }

        [Fact] public void Png_ReadsIhdr() => AssertDims(TestImageBytes.Png(3024, 4032), 3024, 4032);
        [Fact] public void Gif_ReadsLogicalScreen() => AssertDims(TestImageBytes.Gif(640, 480), 640, 480);
        [Fact] public void WebP_Lossy_ReadsVp8() => AssertDims(TestImageBytes.WebPLossy(800, 600), 800, 600);
        [Fact] public void WebP_Lossless_ReadsVp8L() => AssertDims(TestImageBytes.WebPLossless(1080, 1920), 1080, 1920);
        [Fact] public void WebP_Extended_ReadsVp8X() => AssertDims(TestImageBytes.WebPExtended(2560, 1080), 2560, 1080);

        [Fact] public void Jpeg_Baseline_SkipsDhtAndReadsSof0() => AssertDims(TestImageBytes.Jpeg(4032, 3024), 4032, 3024);
        [Fact] public void Jpeg_Progressive_ReadsSof2() => AssertDims(TestImageBytes.Jpeg(4032, 3024, sofMarker: 0xC2), 4032, 3024);

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Jpeg_ExifOrientationUpright_KeepsStoredDimensions(int orientation) =>
            AssertDims(TestImageBytes.Jpeg(4032, 3024, orientation), 4032, 3024);

        [Theory]
        [InlineData(5, false)]
        [InlineData(6, false)]
        [InlineData(7, true)]
        [InlineData(8, true)]
        public void Jpeg_ExifOrientationRotated_SwapsDimensions(int orientation, bool bigEndian) =>
            AssertDims(TestImageBytes.Jpeg(4032, 3024, orientation, bigEndianExif: bigEndian), 3024, 4032);

        [Fact]
        public void Jpeg_ExifOrientationInvalid_IsIgnored() =>
            AssertDims(TestImageBytes.Jpeg(4032, 3024, 9), 4032, 3024);

        [Theory]
        [InlineData(0xFFFFFFFFu)]
        [InlineData(0x7FFFFFFFu)]
        [InlineData(0x80000000u)]
        [InlineData(0xFFFFFFF8u)]
        public void Jpeg_MalformedExifIfdOffset_DoesNotThrowAndIsIgnored(uint ifdOffset)
        {
            var bytes = TestImageBytes.Jpeg(4032, 3024, 6);
            var exif = System.Array.FindIndex(bytes, i => i == (byte)'E');
            var tiff = exif + 6;
            bytes[tiff + 4] = (byte)ifdOffset;
            bytes[tiff + 5] = (byte)(ifdOffset >> 8);
            bytes[tiff + 6] = (byte)(ifdOffset >> 16);
            bytes[tiff + 7] = (byte)(ifdOffset >> 24);

            AssertDims(bytes, 4032, 3024);
        }

        [Fact]
        public void NullEmptyOrUnknown_ReturnFalse()
        {
            Assert.False(ImageDimensionsHelper.TryGetDimensions(null, out _, out _));
            Assert.False(ImageDimensionsHelper.TryGetDimensions(new byte[0], out _, out _));
            Assert.False(ImageDimensionsHelper.TryGetDimensions(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, out var w, out var h));
            Assert.Equal(0, w);
            Assert.Equal(0, h);
        }

        [Fact]
        public void Truncated_NeverThrows()
        {
            foreach (var full in new[] { TestImageBytes.Png(10, 20), TestImageBytes.Jpeg(10, 20, 6), TestImageBytes.WebPLossy(10, 20), TestImageBytes.WebPLossless(10, 20), TestImageBytes.WebPExtended(10, 20), TestImageBytes.Gif(10, 20) })
            {
                for (var len = 0; len < full.Length; len++)
                {
                    var truncated = full.Take(len).ToArray();
                    var ex = Record.Exception(() => ImageDimensionsHelper.TryGetDimensions(truncated, out _, out _));
                    Assert.Null(ex);
                }
            }
        }

        [Fact]
        public void TruncatedBeforeDimensionFields_ReturnsFalse()
        {
            var jpeg = TestImageBytes.Jpeg(10, 20, 6);
            var jpegSof = Enumerable.Range(0, jpeg.Length - 1).First(i => jpeg[i] == 0xFF && jpeg[i + 1] == 0xC0);

            // (full image, number of leading bytes needed to contain its dimension fields)
            var cases = new (byte[] Full, int MinLength)[]
            {
                (TestImageBytes.Png(10, 20), 24),
                (TestImageBytes.Gif(10, 20), 10),
                (jpeg, jpegSof + 9),
                (TestImageBytes.WebPLossy(10, 20), 30),
                (TestImageBytes.WebPLossless(10, 20), 25),
                (TestImageBytes.WebPExtended(10, 20), 30)
            };

            foreach (var (full, minLength) in cases)
            {
                for (var len = 0; len < minLength; len++)
                    Assert.False(ImageDimensionsHelper.TryGetDimensions(full.Take(len).ToArray(), out _, out _), $"length {len} of {minLength}");

                Assert.True(ImageDimensionsHelper.TryGetDimensions(full.Take(minLength).ToArray(), out _, out _), $"length {minLength}");
            }
        }

        [Fact]
        public void ZeroDimensions_ReturnFalse() =>
            Assert.False(ImageDimensionsHelper.TryGetDimensions(TestImageBytes.Png(0, 100), out _, out _));
    }
}
