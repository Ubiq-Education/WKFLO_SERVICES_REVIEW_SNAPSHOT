using System;
using System.Collections.Generic;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.ImageGeneration.Services
{
    /// <summary>Builds minimal image headers (not decodable images) for dimension-detection tests.</summary>
    internal static class TestImageBytes
    {
        public static byte[] Png(int width, int height)
        {
            var b = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
            b.AddRange(BigEndian32(width));
            b.AddRange(BigEndian32(height));
            b.AddRange(new byte[] { 8, 6, 0, 0, 0 });
            return b.ToArray();
        }

        public static byte[] Gif(int width, int height) =>
            new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', (byte)(width & 0xFF), (byte)(width >> 8), (byte)(height & 0xFF), (byte)(height >> 8), 0, 0, 0 };

        /// <param name="orientation">EXIF orientation 1-8 (or an invalid value), or 0 for no EXIF segment.</param>
        public static byte[] Jpeg(int width, int height, int orientation = 0, byte sofMarker = 0xC0, bool bigEndianExif = false)
        {
            var b = new List<byte> { 0xFF, 0xD8 };
            // JFIF APP0 first, as most real files have
            b.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 });
            // a DHT segment (0xC4) must not be mistaken for a frame header
            b.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x04, 0x00, 0x00 });
            if (orientation > 0)
            {
                byte[] U16(int v) => bigEndianExif ? new[] { (byte)(v >> 8), (byte)v } : new[] { (byte)v, (byte)(v >> 8) };
                var tiff = new List<byte>();
                tiff.AddRange(bigEndianExif ? new byte[] { (byte)'M', (byte)'M' } : new byte[] { (byte)'I', (byte)'I' });
                tiff.AddRange(U16(42));
                tiff.AddRange(bigEndianExif ? new byte[] { 0, 0, 0, 8 } : new byte[] { 8, 0, 0, 0 });
                tiff.AddRange(U16(1)); // one IFD entry
                tiff.AddRange(U16(0x0112));
                tiff.AddRange(U16(3));
                tiff.AddRange(bigEndianExif ? new byte[] { 0, 0, 0, 1 } : new byte[] { 1, 0, 0, 0 });
                tiff.AddRange(U16(orientation));
                tiff.AddRange(new byte[] { 0, 0 });
                tiff.AddRange(new byte[] { 0, 0, 0, 0 });
                var payload = new List<byte> { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
                payload.AddRange(tiff);
                var len = payload.Count + 2;
                b.AddRange(new byte[] { 0xFF, 0xE1, (byte)(len >> 8), (byte)len });
                b.AddRange(payload);
            }
            b.AddRange(new byte[] { 0xFF, sofMarker, 0x00, 0x0B, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0 });
            b.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02 });
            return b.ToArray();
        }

        public static byte[] WebPLossy(int width, int height)
        {
            var chunk = new List<byte> { 0x10, 0x00, 0x00, 0x9D, 0x01, 0x2A, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8) };
            return WebP("VP8 ", chunk);
        }

        public static byte[] WebPLossless(int width, int height)
        {
            uint bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
            var chunk = new List<byte> { 0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24) };
            return WebP("VP8L", chunk);
        }

        public static byte[] WebPExtended(int width, int height)
        {
            int w = width - 1, h = height - 1;
            var chunk = new List<byte> { 0, 0, 0, 0, (byte)w, (byte)(w >> 8), (byte)(w >> 16), (byte)h, (byte)(h >> 8), (byte)(h >> 16) };
            return WebP("VP8X", chunk);
        }

        private static byte[] WebP(string fourCc, List<byte> chunk)
        {
            var b = new List<byte> { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
            b.AddRange(fourCc.Select(c => (byte)c));
            b.AddRange(new byte[] { (byte)chunk.Count, 0, 0, 0 });
            b.AddRange(chunk);
            b.AddRange(new byte[] { 0, 0, 0, 0 }); // padding so the minimum-length checks pass
            return b.ToArray();
        }

        private static byte[] BigEndian32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    }
}
