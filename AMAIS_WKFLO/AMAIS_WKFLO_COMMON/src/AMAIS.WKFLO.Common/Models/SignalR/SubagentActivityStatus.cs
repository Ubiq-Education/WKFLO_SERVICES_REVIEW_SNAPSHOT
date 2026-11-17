namespace AMAIS.WKFLO.Common.Models.SignalR
{
    /// <summary>Values of <see cref="SubagentActivityUpdate.Status"/>.</summary>
    public static class SubagentActivityStatus
    {
        public const string Started = "started";

        /// <summary>Bug 7722: the sub-agent stopped to wait for the person to allow or deny a tool call.</summary>
        public const string AwaitingApproval = "awaiting_approval";

        /// <summary>Bug 7722: the sub-agent continues after the person allowed its tool call.</summary>
        public const string Resumed = "resumed";

        public const string Completed = "completed";
        public const string Failed = "failed";

        /// <summary>Bug 7722: the person denied the sub-agent's tool call; it continues without it.</summary>
        public const string Denied = "denied";

        /// <summary>Bug 7722: nobody answered the sub-agent's tool approval in time.</summary>
        public const string Expired = "expired";

        /// <summary>US 7501: the person pressed Stop; the sub-agent's run was cancelled before it finished.</summary>
        public const string Cancelled = "cancelled";
    }
}
