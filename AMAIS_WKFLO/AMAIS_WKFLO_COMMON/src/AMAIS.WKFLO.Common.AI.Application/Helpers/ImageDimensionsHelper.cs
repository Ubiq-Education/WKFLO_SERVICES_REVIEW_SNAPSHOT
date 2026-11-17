namespace AMAIS.WKFLO.Common.AI.Application.Helpers
{
    /// <summary>
    /// Reads an image's pixel dimensions straight from its header bytes (PNG, JPEG, WebP, GIF) without any imaging
    /// library. JPEG EXIF orientation 5-8 (rotated 90/270 degrees) is applied by swapping width and height, so the
    /// result is the dimensions as displayed, which is what phone photos need to be classified as portrait or landscape.
    /// Best-effort header reader: returns false when the header is too short to contain the dimensions or the format is
    /// unrecognized, and never throws. It does not validate the rest of the file, so input truncated after the dimension
    /// fields can still return true.
    /// </summary>
    public static class ImageDimensionsHelper
    {
        public static bool TryGetDimensions(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes == null || bytes.Length < 10)
                return false;

            bool ok;
            if (IsPng(bytes))
                ok = TryReadPng(bytes, out width, out height);
            else if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                ok = TryReadJpeg(bytes, out width, out height);
            else if (IsWebP(bytes))
                ok = TryReadWebP(bytes, out width, out height);
            else if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                ok = TryReadGif(bytes, out width, out height);
            else
                ok = false;

            if (!ok || width <= 0 || height <= 0)
            {
                width = 0;
                height = 0;
                return false;
            }

            return true;
        }

        private static bool IsPng(byte[] b) =>
            b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[12] == (byte)'I' && b[13] == (byte)'H' && b[14] == (byte)'D' && b[15] == (byte)'R';

        private static bool IsWebP(byte[] b) =>
            b.Length >= 16 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
            && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';

        private static bool TryReadPng(byte[] b, out int width, out int height)
        {
            width = ReadInt32BigEndian(b, 16);
            height = ReadInt32BigEndian(b, 20);
            return true;
        }

        private static bool TryReadGif(byte[] b, out int width, out int height)
        {
            width = b[6] | (b[7] << 8);
            height = b[8] | (b[9] << 8);
            return true;
        }

        private static bool TryReadWebP(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b.Length < 25)
                return false;

            var fourCc = ((char)b[12]).ToString() + (char)b[13] + (char)b[14] + (char)b[15];
            switch (fourCc)
            {
                case "VP8 ":
                    // Frame tag (3 bytes), start code 9D 01 2A, then 14-bit width and height (little endian).
                    if (b.Length < 30 || b[23] != 0x9D || b[24] != 0x01 || b[25] != 0x2A)
                        return false;
                    width = (b[26] | (b[27] << 8)) & 0x3FFF;
                    height = (b[28] | (b[29] << 8)) & 0x3FFF;
                    return true;
                case "VP8L":
                    // Signature 0x2F, then 14 bits (width - 1) and 14 bits (height - 1).
                    if (b[20] != 0x2F)
                        return false;
                    var bits = (uint)(b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24));
                    width = (int)(bits & 0x3FFF) + 1;
                    height = (int)((bits >> 14) & 0x3FFF) + 1;
                    return true;
                case "VP8X":
                    if (b.Length < 30)
                        return false;
                    // Flags (1) + reserved (3), then 24-bit canvas width - 1 and height - 1.
                    width = (b[24] | (b[25] << 8) | (b[26] << 16)) + 1;
                    height = (b[27] | (b[28] << 8) | (b[29] << 16)) + 1;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadJpeg(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            var orientation = 1;
            var pos = 2;

            while (pos + 4 <= b.Length)
            {
                if (b[pos] != 0xFF)
                {
                    pos++;
                    continue;
                }

                var marker = b[pos + 1];
                if (marker == 0xFF)
                {
                    pos++; // fill byte
                    continue;
                }

                // Standalone markers without a length: TEM, RSTn, SOI, EOI.
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                {
                    if (marker == 0xD9)
                        return false;
                    pos += 2;
                    continue;
                }

                var length = (b[pos + 2] << 8) | b[pos + 3];
                if (length < 2)
                    return false;

                var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof)
                {
                    if (pos + 9 > b.Length)
                        return false;
                    height = (b[pos + 5] << 8) | b[pos + 6];
                    width = (b[pos + 7] << 8) | b[pos + 8];
                    if (orientation >= 5 && orientation <= 8)
                        (width, height) = (height, width);
                    return true;
                }

                if (marker == 0xE1)
                    orientation = ReadExifOrientation(b, pos + 4, Math.Min(pos + 2 + length, b.Length), orientation);

                pos += 2 + length;
            }

            return false;
        }

        // APP1 payload starts with "Exif\0\0" followed by a TIFF header; orientation is tag 0x0112 in IFD0.
        private static int ReadExifOrientation(byte[] b, int start, int end, int fallback)
        {
            if (end - start < 14
                || b[start] != (byte)'E' || b[start + 1] != (byte)'x' || b[start + 2] != (byte)'i' || b[start + 3] != (byte)'f'
                || b[start + 4] != 0 || b[start + 5] != 0)
                return fallback;

            var tiff = start + 6;
            bool little;
            if (b[tiff] == (byte)'I' && b[tiff + 1] == (byte)'I')
                little = true;
            else if (b[tiff] == (byte)'M' && b[tiff + 1] == (byte)'M')
                little = false;
            else
                return fallback;

            var ifdOffset = ReadUInt32(b, tiff + 4, little);
            if (ifdOffset > (uint)(end - tiff - 2))
                return fallback;
            var ifd = tiff + (int)ifdOffset;

            var count = ReadUInt16(b, ifd, little);
            for (var i = 0; i < count; i++)
            {
                var entry = ifd + 2 + i * 12;
                if (entry + 12 > end)
                    return fallback;
                if (ReadUInt16(b, entry, little) == 0x0112)
                {
                    var value = ReadUInt16(b, entry + 8, little);
                    return value >= 1 && value <= 8 ? value : fallback;
                }
            }

            return fallback;
        }

        private static int ReadInt32BigEndian(byte[] b, int o) =>
            (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        private static int ReadUInt16(byte[] b, int o, bool little) =>
            little ? b[o] | (b[o + 1] << 8) : (b[o] << 8) | b[o + 1];

        private static uint ReadUInt32(byte[] b, int o, bool little) =>
            little
                ? (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24))
                : (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
    }
}
