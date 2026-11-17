using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UploadAgentChatIcon
{
    /// <summary>
    /// Uploads and validates a chat icon image for a tenant agent. Does NOT persist anything onto
    /// AgentChatSetting/BubbleIconPath/ChatbotIconPath — it only uploads and returns the resulting
    /// path; the caller (CMS or wkflo_ui) is responsible for saving that path via its own existing
    /// Save flow.
    /// </summary>
    public class UploadAgentChatIconCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int Id { get; set; }

        /// <summary>
        /// Raw file content. Excluded from Newtonsoft JSON serialization so LoggingBehaviour's
        /// request logging doesn't attempt to serialize an open stream.
        /// </summary>
        [Required(ErrorMessage = ErrorConst.Required)]
        [JsonIgnore]
        public Stream Content { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [StringLength(260, ErrorMessage = ErrorConst.MaximumLength)]
        public string FileName { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [StringLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string ContentType { get; set; }
    }
}
