using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.PublishAgentEvent
{
    /// <summary>
    /// Publishes a cross-agent event notification onto <c>AiWorkerTopicConsts.AgentEventMessageTopic</c>
    /// (story #6480 / task #6485 follow-up) so <c>AgentEventSubscriber</c> can invoke
    /// <c>IAgentInvoker</c> directly, entirely outside the main orchestrator workflow path. Replaces an
    /// earlier version of this endpoint that called <c>DaprClient</c> directly with no MediatR command
    /// in between — follows the same vertical-slice shape as <see cref="InvokeAgent.InvokeAgentCommand"/>.
    /// </summary>
    public class PublishAgentEventCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string AgentRole { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public AgentInvocationInput Input { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string SessionId { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string EventType { get; set; }

        /// <summary>
        /// The calling agent's own widget/non-widget context, resolved by
        /// <c>AgentEventPublisherPlugin</c> while still running inside the calling agent's own turn —
        /// see <c>PublishAgentEventDomainEvent.RootIsWidget</c>. Not user input, so no validation
        /// attribute.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// Bug 7722: set only by the in-process hand-off from an interactive chat (see
        /// <c>PublishAgentEventDomainEvent.ApprovalRootAgentId</c>). Never bound from an HTTP request body.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public int? ApprovalRootAgentId { get; set; }

        /// <summary>
        /// US 7501: set only by the in-process hand-off (see <c>PublishAgentEventDomainEvent.RequestedByTurnStartedAt</c>):
        /// the hand-off's queue time. Never bound from an HTTP request body.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset? RequestedByTurnStartedAt { get; set; }
    }
}
