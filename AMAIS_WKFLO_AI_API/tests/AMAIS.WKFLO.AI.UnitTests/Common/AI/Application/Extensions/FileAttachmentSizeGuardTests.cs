using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using System;
using System.Collections.Generic;
using System.Text;
using AMAIS.WKFLO.Common.Models;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Extensions
{
    public class FileAttachmentSizeGuardTests
    {
        private readonly ITestOutputHelper _output;

        public FileAttachmentSizeGuardTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static byte[] RandomBytes(int length, params byte[] prefix)
        {
            var bytes = new byte[length];
            new Random(7).NextBytes(bytes);
            prefix.CopyTo(bytes, 0);
            return bytes;
        }

        private static long Allocated(Action action)
        {
            action(); // warm up
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string BuildDataUri(string mimeType, int base64Length)
        {
            // 'A' repeated is valid base64 alphabet; base64Length divisible by 4 avoids padding,
            // making the decoded-size estimate exact: decodedBytes = base64Length / 4 * 3.
            return $"data:{mimeType};base64,{new string('A', base64Length)}";
        }

        private static string BuildDataUri(string mimeType, byte[] bytes)
            => $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";

        private static byte[] HeicBytes() =>
            [.. new byte[] { 0x00, 0x00, 0x00, 0x18 }, .. Encoding.ASCII.GetBytes("ftyp"), .. Encoding.ASCII.GetBytes("heic")];

        [Fact]
        public void FindOversizedImage_AllFilesWithinLimit_ReturnsNull()
        {
            var files = new List<FileAttachment>
            {
                new() { FileName = "small.png", FileData = BuildDataUri("image/png", 40) }
            };

            var result = files.FindOversizedImage();

            Assert.Null(result);
        }

        [Fact]
        public void FindOversizedImage_ImageOverLimit_ReturnsThatFile()
        {
            // base64Length 14,000,000 -> ~10,500,000 decoded bytes, over the 5,000,000 constant.
            var oversized = new FileAttachment { FileName = "big.png", FileData = BuildDataUri("image/png", 14_000_000) };
            var files = new List<FileAttachment>
            {
                new() { FileName = "small.png", FileData = BuildDataUri("image/png", 40) },
                oversized
            };

            var result = files.FindOversizedImage();

            Assert.Same(oversized, result);
        }

        [Fact]
        public void FindOversizedImage_OversizedNonImageFile_IsIgnored()
        {
            var files = new List<FileAttachment>
            {
                new() { FileName = "big.pdf", FileData = BuildDataUri("application/pdf", 14_000_000) }
            };

            var result = files.FindOversizedImage();

            Assert.Null(result);
        }

        [Fact]
        public void FindOversizedImage_NullOrEmptyList_ReturnsNull()
        {
            Assert.Null(((List<FileAttachment>)null).FindOversizedImage());
            Assert.Null(new List<FileAttachment>().FindOversizedImage());
        }

        [Fact]
        public void FindUnsupportedImageFormat_HeicAttachment_ReturnsThatFile()
        {
            // HEIC is the default iPhone camera format — not usable by any configured provider.
            var heicFile = new FileAttachment { FileName = "photo.heic", FileData = BuildDataUri("image/heic", HeicBytes()) };
            var files = new List<FileAttachment>
            {
                new() { FileName = "small.png", FileData = BuildDataUri("image/png", 40) },
                heicFile
            };

            var result = files.FindUnsupportedImageFormat();

            Assert.Same(heicFile, result);
        }

        [Fact]
        public void FindUnsupportedImageFormat_AllFilesSupported_ReturnsNull()
        {
            var files = new List<FileAttachment>
            {
                new() { FileName = "small.png", FileData = BuildDataUri("image/png", 40) }
            };

            var result = files.FindUnsupportedImageFormat();

            Assert.Null(result);
        }

        [Fact]
        public void FindOversizedImage_AndFindUnsupportedImageFormat_DoNotCopyLargePdfPayload()
        {
            var files = new List<FileAttachment>
            {
                new() { FileName = "big.pdf", FileData = BuildDataUri("application/pdf", RandomBytes(11 * 1024 * 1024, 0x25, 0x50, 0x44, 0x46)) }
            };

            var oversizedAllocated = Allocated(() => Assert.Null(files.FindOversizedImage()));
            var unsupportedAllocated = Allocated(() => Assert.Null(files.FindUnsupportedImageFormat()));

            _output.WriteLine($"PDF: FindOversizedImage {oversizedAllocated:N0} B, FindUnsupportedImageFormat {unsupportedAllocated:N0} B");
            Assert.True(oversizedAllocated < 1_000_000, $"FindOversizedImage allocated {oversizedAllocated:N0} bytes");
            Assert.True(unsupportedAllocated < 1_000_000, $"FindUnsupportedImageFormat allocated {unsupportedAllocated:N0} bytes");
        }

        [Fact]
        public void FindOversizedImage_AndFindUnsupportedImageFormat_DoNotCopyLargeImagePayload()
        {
            var big = new FileAttachment { FileName = "big.png", FileData = BuildDataUri("image/png", RandomBytes(11 * 1024 * 1024, 0x89, 0x50, 0x4E, 0x47)) };
            var files = new List<FileAttachment> { big };

            var oversizedAllocated = Allocated(() => Assert.Same(big, files.FindOversizedImage()));
            var unsupportedAllocated = Allocated(() => Assert.Null(files.FindUnsupportedImageFormat()));

            _output.WriteLine($"Image: FindOversizedImage {oversizedAllocated:N0} B, FindUnsupportedImageFormat {unsupportedAllocated:N0} B");
            Assert.True(oversizedAllocated < 1_000_000, $"FindOversizedImage allocated {oversizedAllocated:N0} bytes");
            Assert.True(unsupportedAllocated < 1_000_000, $"FindUnsupportedImageFormat allocated {unsupportedAllocated:N0} bytes");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FindOversizedImage_DecodedSizeAtLimit_IsNotOversized_AndOneOverIs(int extraBytes)
        {
            // Covers 0, 1 and 2 padding characters: limit-of-the-form 3k+extra decoded bytes.
            var limit = (int)CommonConsts.CommonImageAttachmentConsts.MaxSingleImageSizeBytes;
            var atLimit = limit - (limit % 3) + extraBytes; // may exceed the limit by up to 2 -> compute below
            if (atLimit > limit)
                atLimit -= 3;

            var within = new List<FileAttachment> { new() { FileData = BuildDataUri("image/jpeg", new byte[atLimit]) } };
            var over = new List<FileAttachment> { new() { FileData = BuildDataUri("image/jpeg", new byte[limit + 1]) } };

            Assert.Null(within.FindOversizedImage());
            Assert.NotNull(over.FindOversizedImage());
        }

        [Fact]
        public void FindOversizedImage_RawBase64WithoutHeader_IsDetectedByContent()
        {
            var limit = (int)CommonConsts.CommonImageAttachmentConsts.MaxSingleImageSizeBytes;
            var raw = Convert.ToBase64String(RandomBytes(limit + 10, 0x89, 0x50, 0x4E, 0x47));
            var files = new List<FileAttachment> { new() { FileData = raw } };

            Assert.NotNull(files.FindOversizedImage());
        }

        [Fact]
        public void FindUnsupportedImageFormat_RawBase64Heic_IsNotFlagged()
        {
            var files = new List<FileAttachment> { new() { FileData = Convert.ToBase64String(HeicBytes()) } };
            // Raw base64 HEIC is not sniffed as an image MIME type, so it is not flagged (unchanged behaviour).
            Assert.Null(files.FindUnsupportedImageFormat());
        }

        [Theory]
        [InlineData("bmp")]
        [InlineData("tiff")]
        public void FindUnsupportedImageFormat_BmpAndTiff_AreFlagged(string kind)
        {
            var bytes = kind == "bmp"
                ? RandomBytes(64, 0x42, 0x4D)
                : RandomBytes(64, 0x49, 0x49, 0x2A, 0x00);
            var attachment = new FileAttachment { FileData = BuildDataUri("image/" + kind, bytes) };

            Assert.Same(attachment, new List<FileAttachment> { attachment }.FindUnsupportedImageFormat());
        }
    }
}
