using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Models;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    /// <summary>
    /// Bug 8189 / Task 8219: <see cref="FileAttachment.SourceDocumentId"/> and <see cref="FileAttachment.SourceDocument"/> are set only by the
    /// server-side hand-off (<c>AgentEventPublisherPlugin</c>); they bind from request bodies like any property, so every HTTP-facing
    /// entry point clears them before the message is queued or run. (The copy is scoped to the caller's own tenant, session and user
    /// anyway; this keeps a caller from pointing it at a document id of its own choosing.)
    /// </summary>
    public static class FileAttachmentServerFields
    {
        public static void ClearServerOnlyFields(this IEnumerable<FileAttachment> files)
        {
            if (files == null)
                return;

            foreach (var file in files)
            {
                if (file == null)
                    continue;

                file.SourceDocumentId = null;
                file.SourceDocument = null;
            }
        }
    }

    public static class FileAttachmentSizeGuard
    {
        /// <summary>
        /// Returns the first image attachment whose decoded size exceeds
        /// CommonImageAttachmentConsts.MaxSingleImageSizeBytes, or null if all attachments are within
        /// the limit. Only image attachments are checked — Anthropic's 10 MB cap is image-specific.
        /// </summary>
        public static FileAttachment FindOversizedImage(this List<FileAttachment> files)
        {
            if (files == null)
                return null;

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file?.FileData))
                    continue;

                var mimeType = file.FileData.GetMimeTypeWithoutCopy(out var payloadOffset);
                if (!mimeType.IsImageMimeType())
                    continue;

                if (EstimateDecodedByteCount(file.FileData, payloadOffset) > CommonConsts.CommonImageAttachmentConsts.MaxSingleImageSizeBytes)
                    return file;
            }

            return null;
        }

        /// <summary>
        /// Returns the first image attachment whose actual bytes are a recognized-but-unsupported
        /// image format (HEIC/HEIF, BMP, TIFF — none of our providers can process these regardless of
        /// what MIME type is declared), or null if none. Most commonly a photo straight off an iPhone
        /// camera (HEIC is its default format).
        /// </summary>
        public static FileAttachment FindUnsupportedImageFormat(this List<FileAttachment> files)
        {
            if (files == null)
                return null;

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file?.FileData))
                    continue;

                var mimeType = file.FileData.GetMimeTypeWithoutCopy(out _);
                if (!mimeType.IsImageMimeType())
                    continue;

                if (file.FileData.IsUnsupportedImageFormat())
                    return file;
            }

            return null;
        }

        // Works on the original string (no Substring of the payload): attachments can be tens of MB.
        private static long EstimateDecodedByteCount(string fileData, int payloadOffset)
        {
            var payloadLength = fileData.Length - payloadOffset;
            if (payloadLength <= 0)
                return 0;

            var padding = fileData.EndsWith("==", StringComparison.Ordinal) ? 2
                : fileData.EndsWith('=') ? 1 : 0;
            return (long)payloadLength / 4 * 3 - padding;
        }
    }
}
