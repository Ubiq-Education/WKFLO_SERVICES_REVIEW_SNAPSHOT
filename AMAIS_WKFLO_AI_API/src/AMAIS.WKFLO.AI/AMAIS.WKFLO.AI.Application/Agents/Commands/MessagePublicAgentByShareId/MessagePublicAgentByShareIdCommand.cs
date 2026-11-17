using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Queue;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId
{
    public class MessagePublicAgentByShareIdCommand : IWkfloRequest<Result>, IRequestShareId<string>, IWkfloQueueMessageCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string ShareId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public MessagePublicAgentInputCommand Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        /// <summary>
        /// True when this request is wkflo_ui's "replay" of an earlier message whose answer was
        /// deferred behind the ContactForm tool (turn 1 only returns filler since the agent can't
        /// answer pre-registration) — sent once the visitor completes the form, with the literal
        /// original question text, to obtain the real answer now that registration is complete. Default
        /// <c>false</c> for every ordinary chat message. See
        /// <see cref="AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models.RunTextOptions.IsContactFormReplay"/>
        /// for how this is honored: the turn still runs the model, but is not recorded as a second,
        /// independent visitor message (no duplicate <c>AuthorRole.User</c> ChatHistory entry, no second
        /// "Chatbot Message Topic" classification for the same already-classified question).
        /// </summary>
        public bool IsContactFormReplay { get; set; }

        public string GetId()
        {
            return SessionId;
        }
    }

    public class MessagePublicAgentInputCommand
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        public List<FileAttachment> Files { get; set; }
    }
}
