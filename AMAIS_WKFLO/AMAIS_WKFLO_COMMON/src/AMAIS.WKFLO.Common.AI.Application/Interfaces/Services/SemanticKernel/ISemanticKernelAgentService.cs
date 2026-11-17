using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Models.AI;

namespace AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel
{
    public interface ISemanticKernelAgentService
    {
        public Task<AgentAnswer> RunTextAsync(RunTextOptions options, CancellationToken cancellationToken);
        public Task<AgentAnswer> RunVoiceAsync(RunVoiceOptions options, CancellationToken cancellationToken);

        /// <summary>
        /// US 7502: appends one "Stopped" marker for the Stop at <paramref name="stoppedAt"/> to the agent's saved history
        /// (loaded first, never replaced). Saves nothing when the agent has no saved history in the session, or when it
        /// already holds a marker of the same Stop. Used for the chat agent when queued hand-offs were dropped. Returns
        /// what happened to the marker. Never throws.
        /// </summary>
        public Task<Domain.Agents.ChatStop.StoppedMarkerOutcome> SaveStoppedMarkerAsync(MD.Domain.Entities.RunnableAgent agent, string sessionId, DateTimeOffset stoppedAt);

        /// <summary>
        /// US 7502: a turn stopped while it was still queued (it never reached the run). Sends the "Stopped" marker live
        /// and saves it after the agent's existing history (the question is not stored; nothing is saved when the agent
        /// has no saved history yet). Never throws.
        /// </summary>
        public Task SaveTurnStoppedBeforeRunAsync(MD.Domain.Entities.RunnableAgent agent, string sessionId, bool? rootIsWidget);
    }
}
