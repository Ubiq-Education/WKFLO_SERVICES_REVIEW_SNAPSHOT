using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.InvokeAgent
{
    /// <summary>
    /// Runs a single runnable agent (<c>TenantAgent</c> or <c>PublishedTenantAgent</c>) as one
    /// step/role of a multi-agent Dapr Workflow orchestration. This is the only entry point
    /// <see cref="Invocation.IAgentInvoker"/> uses to reach the Semantic Kernel execution path — it
    /// delegates to <c>IRunnableAISyncService</c> instead of duplicating agent-loading/provider
    /// resolution logic.
    /// </summary>
    public class InvokeAgentCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        /// <summary>
        /// Free-form role label assigned by the orchestrator (e.g. "researcher", "critic").
        /// </summary>
        public string AgentRole { get; set; }

        /// <summary>
        /// Text + optional file attachments for the agent. Required (see
        /// <see cref="InvokeAgentCommandValidator"/>) — validated via FluentValidation rather than a
        /// DataAnnotations attribute here, since <see cref="AgentInvocationInput"/> is shared with
        /// non-HTTP-bound consumers (actor request, workflow input) where DataAnnotations never runs.
        /// </summary>
        public AgentInvocationInput Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        public bool ChainOfThought { get; set; }

        /// <summary>
        /// See <see cref="AgentInvocationRequest.RootIsWidget"/> — forwarded by
        /// <see cref="Invocation.IAgentInvoker"/> so <c>IRunnableAISyncService</c> resolves this
        /// subagent's chat icon in the same widget/non-widget context as the hand-off's root agent.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>Bug 7722: see <c>AgentInvocationRequest.ApprovalRootAgentId</c>.</summary>
        public int? ApprovalRootAgentId { get; set; }
    }
}
