using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById
{
    public class MessageTenantAgentByIdCommand : IWkfloRequest<Result>, IRequestId<int>, IWkfloQueueMessageCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        public bool ChainOfThought { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public MessageTenantAgentInputCommand Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        /// <summary>
        /// See <see cref="AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId.MessagePublicAgentByShareIdCommand.IsContactFormReplay"/> —
        /// wkflo_ui's shared `use-chat.ts` hook is used by both the public widget and the authenticated
        /// tenant-facing app, so this tenant-side command needs the identical flag for the same
        /// ContactForm "replay" turn to avoid duplicating a chat turn. Default <c>false</c> for every
        /// ordinary chat message.
        /// </summary>
        public bool IsContactFormReplay { get; set; }

        public string GetId()
        {
            return SessionId;
        }
    }

    public class MessageTenantAgentInputCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public List<FileAttachment> Files { get; set; }
    }
}
