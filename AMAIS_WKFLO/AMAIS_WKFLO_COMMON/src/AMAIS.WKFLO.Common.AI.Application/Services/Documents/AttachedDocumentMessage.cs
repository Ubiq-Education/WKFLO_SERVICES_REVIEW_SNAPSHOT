using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.Documents
{
    /// <summary>
    /// The short history message that stands in for a large document (Bug 8189 / Task 8219): its text, the metadata that
    /// keeps the chat's file card, forwarding and the memory handlers working, and the reading of both back from a loaded history.
    /// </summary>
    public static class AttachedDocumentMessage
    {
        /// <summary>The start of a document message that carries the whole extracted text (small documents, and every message saved before Task 8219).</summary>
        public const string InlinePrefix = "Document content:\n";

        private const string SummaryLabel = "Summary: ";

        /// <summary>The model-facing text: what the document is, where its content is, and its summary (or, if it could not be indexed, an excerpt).</summary>
        public static string BuildText(AttachedDocumentInfo document)
        {
            var sb = new StringBuilder();
            sb.Append($"Attached document: '{document.FileName}' ({document.FileType}, {FormatSize(document.FileSize)}, ~{FormatCount(document.CharCount)} characters");
            if (document.PageCount is > 0)
                sb.Append($", {document.PageCount.Value.ToString("N0", CultureInfo.InvariantCulture)} pages");
            sb.AppendLine(").");

            if (document.IndexStatus == ChatDocumentIndexStatus.Failed)
            {
                sb.AppendLine("The document could not be indexed, so only this excerpt, sampled from different places of it, is available:");
                if (!string.IsNullOrEmpty(document.Summary))
                    sb.AppendLine($"{SummaryLabel}{document.Summary}");
                sb.Append(document.Excerpt);
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("The full text is not in this conversation; relevant passages are supplied with each message.");
            if (document.IndexStatus == ChatDocumentIndexStatus.Partial)
            {
                sb.AppendLine($"Only {document.IndexedChunkCount.ToString("N0", CultureInfo.InvariantCulture)} of its {document.ChunkCount.ToString("N0", CultureInfo.InvariantCulture)} sections are searchable (spread evenly over the whole document).");
            }

            if (!string.IsNullOrEmpty(document.Summary))
                sb.Append($"{SummaryLabel}{document.Summary}");

            return sb.ToString().TrimEnd();
        }

        /// <summary>The metadata of the message's text item: the file card fields plus the document keys.</summary>
        public static Dictionary<string, object> BuildItemMetadata(AttachedDocumentInfo document) => new()
        {
            { CommonMetadataConsts.FileName, document.FileName },
            { CommonMetadataConsts.FileType, document.FileType },
            { CommonMetadataConsts.FileSize, document.FileSize },
            { CommonMetadataConsts.DocumentId, document.DocumentId.ToString("D") },
            { CommonMetadataConsts.DocumentCharCount, document.CharCount },
            { CommonMetadataConsts.DocumentChunkCount, document.ChunkCount },
            { CommonMetadataConsts.DocumentIndexedChunkCount, document.IndexedChunkCount },
            { CommonMetadataConsts.DocumentIndexStatus, document.IndexStatus }
        };

        /// <summary>The documents the history holds as short messages, in order. Metadata read back from the blob arrives as JSON elements; both forms are read.</summary>
        public static List<AttachedDocumentInfo> ReadDocuments(ChatHistory history)
        {
            var documents = new List<AttachedDocumentInfo>();
            foreach (var message in history)
            {
                if (TryRead(message, out var document))
                    documents.Add(document);
            }

            return documents;
        }

        public static bool TryRead(ChatMessageContent message, out AttachedDocumentInfo document)
        {
            document = null;
            if (message.Role != AuthorRole.User)
                return false;

            foreach (var item in message.Items.OfType<TextContent>())
            {
                var metadata = item.Metadata;
                if (metadata == null || !Guid.TryParse(AsString(metadata, CommonMetadataConsts.DocumentId), out var id))
                    continue;

                var text = item.Text ?? string.Empty;
                var summaryAt = text.IndexOf(SummaryLabel, StringComparison.Ordinal);

                document = new AttachedDocumentInfo
                {
                    DocumentId = id,
                    FileName = AsString(metadata, CommonMetadataConsts.FileName),
                    FileType = AsString(metadata, CommonMetadataConsts.FileType),
                    FileSize = AsInt(metadata, CommonMetadataConsts.FileSize),
                    CharCount = AsInt(metadata, CommonMetadataConsts.DocumentCharCount),
                    ChunkCount = AsInt(metadata, CommonMetadataConsts.DocumentChunkCount),
                    IndexedChunkCount = AsInt(metadata, CommonMetadataConsts.DocumentIndexedChunkCount),
                    IndexStatus = AsString(metadata, CommonMetadataConsts.DocumentIndexStatus),
                    Summary = summaryAt >= 0 ? text[(summaryAt + SummaryLabel.Length)..] : null
                };

                // A failed document's text holds its excerpt after the summary: not a summary to carry on.
                if (document.IndexStatus == ChatDocumentIndexStatus.Failed)
                    document.Summary = null;
                return true;
            }

            return false;
        }

        /// <summary>Whether the message is a user message holding a document's whole text inline (the "Document content:" format).</summary>
        public static bool TryReadInline(ChatMessageContent message, out TextContent item)
        {
            item = null;
            if (message.Role != AuthorRole.User)
                return false;

            item = message.Items.OfType<TextContent>().FirstOrDefault(t =>
                t.Text != null && t.Text.StartsWith(InlinePrefix, StringComparison.Ordinal)
                && t.Metadata != null && t.Metadata.ContainsKey(CommonMetadataConsts.FileName));
            return item != null;
        }

        private static string AsString(IReadOnlyDictionary<string, object> metadata, string key)
        {
            if (!metadata.TryGetValue(key, out var value) || value == null)
                return null;

            return value is JsonElement element ? element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString() : value.ToString();
        }

        private static int AsInt(IReadOnlyDictionary<string, object> metadata, string key) =>
            int.TryParse(AsString(metadata, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

        private static string FormatSize(int bytes) => bytes switch
        {
            >= 1_048_576 => $"{(bytes / 1_048_576.0).ToString("0.0", CultureInfo.InvariantCulture)} MB",
            >= 1_024 => $"{(bytes / 1_024.0).ToString("0", CultureInfo.InvariantCulture)} KB",
            _ => $"{bytes} bytes"
        };

        private static string FormatCount(int count) => count switch
        {
            >= 1_000_000 => $"{(count / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture)}M",
            >= 10_000 => $"{(count / 1_000.0).ToString("0", CultureInfo.InvariantCulture)}K",
            _ => count.ToString("N0", CultureInfo.InvariantCulture)
        };
    }
}
