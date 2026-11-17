using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models
{
    public class FileAttachment
    {
        /// <summary>Base64 or data URI; can be tens of MB, so it is left out of logged requests.</summary>
        [OmitFromLogs]
        public string FileData { get; set; }

        public string FileName { get; set; }

        public int FileSize { get; set; }

        /// <summary>
        /// Plain blob URL set after the image is uploaded to the chat-images container.
        /// Populated by SemanticKernelAgentService.RunAsync before passing files to the
        /// Run and Reasoning private methods. Null for non-image files.
        /// </summary>
        public string BlobUrl { get; set; }

        /// <summary>
        /// Bug 8189 / Task 8219: set when the attachment is a document that another agent already holds in this chat's
        /// document store (a hand-off). <see cref="FileData"/> is then empty: the target agent gets its own copy of the chunks.
        /// </summary>
        public Guid? SourceDocumentId { get; set; }

        /// <summary>The source document's details for a hand-off (<see cref="SourceDocumentId"/>): type, counts, status and summary.</summary>
        public AttachedDocumentInfo SourceDocument { get; set; }

        // Everything below is set while the turn processes the attachment and never leaves the process.

        /// <summary>The detected MIME type of a document, kept after <see cref="FileData"/> is released.</summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public string FileType { get; set; }

        /// <summary>The extracted text of a document short enough to stay inline in the history; the file data is released once it is read.</summary>
        [OmitFromLogs, JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public string ExtractedText { get; set; }

        /// <summary>The indexed document that stands in for a large document's text in the history.</summary>
        [JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
        public AttachedDocumentInfo Document { get; set; }
    }
}
