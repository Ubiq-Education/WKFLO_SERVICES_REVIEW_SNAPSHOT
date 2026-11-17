namespace AMAIS.WKFLO.Common.AI.Application.Helpers
{
    /// <summary>
    /// Sniffs an image's MIME type/extension from its own magic bytes, for the (common) case where a
    /// declared MIME type isn't available — e.g. <c>ImageContent.MimeType</c> is null whenever
    /// <c>IChatStoreService.LoadChatAsync</c> reconstructs it from stored blob JSON (only the Uri
    /// survives that round-trip), or when a provider's raw image response doesn't declare one.
    /// </summary>
    public static class ImageMimeTypeHelper
    {
        /// <summary>
        /// Detects PNG, JPEG, WebP, and GIF from their magic bytes; defaults to PNG when the format
        /// isn't recognized (including empty/too-short input) rather than throwing, since callers use
        /// this as a best-effort label on bytes they already have, not as format validation.
        /// </summary>
        public static (string MimeType, string Extension) DetectFromBytes(byte[] bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ("image/png", "png");
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return ("image/jpeg", "jpeg");
            // WebP: RIFF header at 0-3, WEBP at 8-11
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return ("image/webp", "webp");
            if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return ("image/gif", "gif");
            return ("image/png", "png");
        }
    }
}
