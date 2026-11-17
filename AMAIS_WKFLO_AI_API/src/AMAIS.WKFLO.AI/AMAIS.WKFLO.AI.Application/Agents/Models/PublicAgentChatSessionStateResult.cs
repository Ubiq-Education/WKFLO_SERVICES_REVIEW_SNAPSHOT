namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    /// <summary>
    /// US 7506: live state of a chat session.
    /// </summary>
    public class PublicAgentChatSessionStateResult
    {
        /// <summary>
        /// True while any agent of the session is running a turn or has a hand-off waiting in its queue.
        /// </summary>
        public bool IsWorking { get; set; }
    }
}
