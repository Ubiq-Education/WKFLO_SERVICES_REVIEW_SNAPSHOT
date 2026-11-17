using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.Common.Models.Queue
{
    public class WkfloQueueMessage<IWkfloQueueMessageCommand>
    {
        public IWkfloQueueMessageCommand Command { get; set; }
        public string InstanceId { get; set; }
        public TenantContext TenantContext { get; set; }
        public ChatContext ChatContext { get; set; }
        public UserContext UserContext { get; set; }
        public AgentSharableContext AgentSharableContext { get; set; }

        /// <summary>
        /// US 7501: when the message was queued (UTC). A Stop cancels the session's turns requested before it, including
        /// one still waiting in the queue; null for producers that don't set it (the activity start time is used then).
        /// </summary>
        public DateTimeOffset? QueuedAt { get; set; }
    }

    public interface IWkfloQueueMessageCommand : IWkfloRequest<Result>
    {
        string GetId();
    }
}
