namespace AMAIS.WKFLO.Common.Models.AI
{
    public class AgentAnswer
    {
        public string MessageId { get; set; }
        public string Message { get; set; }
        public AITokenUsage TokenUsage { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string AgentType { get; set; }

        /// <summary>
        /// The agent's full set of tags (WI 7365), reported to PUM alongside <see cref="AgentType"/> so
        /// consumers keep the complete tag list even though <see cref="AgentType"/> only carries the
        /// joined string used for <c>SourceName</c>. May be null/empty when the agent has no tags — see
        /// the call sites in SemanticKernelAgentServiceRun(Voice).cs.
        /// </summary>
        public List<string> AgentTags { get; set; }

        /// <summary>
        /// Stable identifier of the agent (WI 7365) — unlike <see cref="AgentType"/>/<see cref="AgentTags"/>,
        /// which can change over time as tags/names are edited, this never changes for a given agent.
        /// Null when the answer wasn't produced from a full <c>Agent</c> entity (e.g. ImageGenerationPlugin,
        /// which only carries a pre-joined agent type string).
        /// </summary>
        public int? AgentId { get; set; }

        /// <summary>
        /// Id of the <c>AIModel</c> actually used for this call (WI 7303), used to look up the model's
        /// <c>AICredit</c> pricing so real per-call credits can be reported to PUM alongside token usage.
        /// Null when the answer wasn't produced from a full <c>Agent</c> entity with an <c>AIModel</c> nav
        /// loaded (e.g. ImageGenerationPlugin, which only carries plain provider/model strings).
        /// </summary>
        public int? AIModelId { get; set; }

        public AgentAnswer()
        {
            TokenUsage = new AITokenUsage();
        }
    }
}
