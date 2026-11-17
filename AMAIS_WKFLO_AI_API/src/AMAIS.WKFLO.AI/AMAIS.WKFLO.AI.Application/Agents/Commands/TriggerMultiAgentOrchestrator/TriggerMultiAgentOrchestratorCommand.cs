using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator
{
    /// <summary>
    /// Publishes a request to start a <see cref="Workflows.MultiAgentOrchestrator.MultiAgentOrchestratorWorkflow"/>
    /// run (story #6480) — closes the "no trigger exists for this workflow" gap documented since task
    /// #6488. Follows the same <c>WkfloQueueMessage</c>/state-store-key-indirection publish strategy as
    /// <c>TenantAgentChatMessage</c>/<c>PublicAgentChatMessage</c> (see
    /// <see cref="TriggerMultiAgentOrchestratorCommandHandler"/>), because the message broker backing
    /// <c>pubsub-ai</c> has a payload size limit and this command's <see cref="Input"/> text can be
    /// arbitrarily long.
    /// </summary>
    public class TriggerMultiAgentOrchestratorCommand : IWkfloRequest<Result>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public AgentInvocationInput Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public MultiAgentOrchestratorStep LeadAgent { get; set; }

        public List<MultiAgentOrchestratorStep> FanOutAgents { get; set; }

        public bool ChainOfThought { get; set; }

        /// <summary>
        /// The lead agent's own widget/non-widget context, resolved once by
        /// <see cref="TriggerMultiAgentOrchestratorCommandHandler"/> at trigger time — see
        /// <c>MultiAgentOrchestratorWorkflowInput.RootIsWidget</c>. Not user input, so no validation
        /// attribute.
        /// </summary>
        public bool? RootIsWidget { get; set; }
    }
}
