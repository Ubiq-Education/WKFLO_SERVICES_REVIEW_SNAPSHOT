namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Workflows
{
    public class MessageResolutionWorkflowInput
    {
        public string TenantName { get; set; }
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public Guid MessageId { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }

        /// <summary>
        /// The MessageId of the assistant's own answer ChatMessageContent — distinct from <see cref="MessageId"/>
        /// (the user's question). Persisted onto <c>MessageTopicClassification.AnswerMessageId</c> once judged,
        /// so a later rating (always given on the answer) can look this row up directly.
        /// </summary>
        public Guid AnswerMessageId { get; set; }
    }
}
