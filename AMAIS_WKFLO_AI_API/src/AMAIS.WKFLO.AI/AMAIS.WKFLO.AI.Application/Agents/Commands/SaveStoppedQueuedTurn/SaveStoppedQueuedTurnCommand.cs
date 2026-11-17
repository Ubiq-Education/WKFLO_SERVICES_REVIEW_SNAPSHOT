using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.SaveStoppedQueuedTurn
{
    /// <summary>
    /// US 7502: a turn the user stopped while it was still queued, so it never reached the run. Saves only the "Stopped"
    /// marker after the agent's existing history (the question is not stored). Internal: sent by the workflow
    /// activities, never from an endpoint. The agent is found by <see cref="AgentId"/> (tenant or published agent) or by
    /// <see cref="ShareId"/> (public chat), always within the current tenant.
    /// </summary>
    public class SaveStoppedQueuedTurnCommand : IWkfloRequest<Result>
    {
        public int? AgentId { get; set; }
        public string ShareId { get; set; }
        public string SessionId { get; set; }

        /// <summary>The chat's widget context when already known (hand-offs); resolved from the share otherwise.</summary>
        public bool? RootIsWidget { get; set; }
    }
}
