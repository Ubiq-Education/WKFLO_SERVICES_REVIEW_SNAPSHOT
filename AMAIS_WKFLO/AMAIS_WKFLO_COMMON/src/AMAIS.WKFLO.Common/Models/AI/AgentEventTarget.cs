namespace AMAIS.WKFLO.Common.Models.AI
{
    /// <summary>
    /// One agent to notify via <c>AgentEventPublisherPlugin.PublishAgentEventAsync</c> — the LLM
    /// supplies a JSON array of these, naming each target agent by <see cref="AgentName"/> (the LLM has
    /// no natural way to know a numeric agent id). The plugin resolves each name to a real agent id via
    /// a tenant-scoped lookup before publishing.
    /// </summary>
    public class AgentEventTarget
    {
        public string AgentName { get; set; }
    }
}
