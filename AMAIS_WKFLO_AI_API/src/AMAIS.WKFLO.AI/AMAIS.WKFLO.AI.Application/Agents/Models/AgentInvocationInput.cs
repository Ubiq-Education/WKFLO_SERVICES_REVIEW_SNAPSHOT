using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// Shared input shape (text + optional file attachments) for every layer of the multi-agent
    /// pipeline — <see cref="Commands.InvokeAgent.InvokeAgentCommand"/>, <see cref="AgentInvocationRequest"/>,
    /// <see cref="MultiAgentOrchestratorWorkflowInput"/>, and <see cref="AgentEventMessage"/> — mirroring
    /// the existing <c>MessageTenantAgentInputCommand</c>/<c>MessagePublicAgentInputCommand</c>
    /// <c>{Text, Files}</c> convention. Kept as one shared type rather than a duplicate nested class
    /// per file (unlike those two, which are genuinely independent commands) because these are all
    /// different envelopes of the same data moving through one feature's actor/workflow/pub-sub
    /// layers. <see cref="Text"/> carries <c>[Required]</c> so any HTTP-facing request/command that
    /// embeds this type (e.g. <c>Commands.PublishAgentEvent.PublishAgentEventCommand</c>) gets
    /// automatic recursive model-state validation from <c>[ApiController]</c> — for non-HTTP-bound
    /// consumers (workflow input, actor request) that attribute is simply inert, and
    /// <see cref="Commands.InvokeAgent.InvokeAgentCommandValidator"/>/<c>PublishAgentEventCommandValidator</c>
    /// separately enforce the same rule via FluentValidation for commands dispatched via
    /// <c>mediator.Send</c> rather than HTTP model binding.
    /// </summary>
    public class AgentInvocationInput
    {
        /// <summary>
        /// The text prompt/task for the agent.
        /// </summary>
        [Required(ErrorMessage = ErrorConst.Required)]
        public string Text { get; set; }

        /// <summary>
        /// Optional file attachments accompanying the text, forwarded as-is to
        /// <c>IRunnableAISyncService.RunRunnableAgentByIdAsync</c>.
        /// </summary>
        public List<FileAttachment> Files { get; set; }
    }
}
