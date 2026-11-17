using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadSiblingAgentContextBySession
{
    /// <summary>
    /// US 7078 (agent-to-agent context awareness): pulls the full injectable output authored by
    /// agents the current (root) <see cref="Agent"/> is explicitly authorized to spin up (see
    /// <see cref="AgentAllowedSpinUpAgent"/>) within the same <c>SessionId</c> — e.g. MAISY
    /// automatically becoming aware of an image Vera just generated/edited, without the school admin
    /// manually downloading and re-uploading it.
    ///
    /// Reuses the existing spin-up allowlist/flag from story #6480 as-is (no new schema): gated on
    /// <see cref="RunnableAgent.EnableSpinUpAgents"/>, scoped to <see cref="AgentAllowedSpinUpAgent"/>
    /// rows only — never "any other agent present in this session." Deliberately unbounded by time —
    /// every turn re-pulls a child's entire injectable history for the session, not just what's new
    /// since the root's last turn. See <c>LoadSiblingAgentContextBySessionQueryHandler</c> for the
    /// full mechanism and <c>AMAIS_WKFLO_AI_API/docs/multi-agent-orchestration.md</c> for background
    /// on the mechanism this extends.
    /// </summary>
    public class LoadSiblingAgentContextBySessionQuery : IWkfloRequest<List<ChatMessageContent>>
    {
        [JsonIgnore]
        public RunnableAgent Agent { get; set; }

        [Required(ErrorMessage = CommonErrorConst.Required)]
        public string SessionId { get; set; }
    }
}
