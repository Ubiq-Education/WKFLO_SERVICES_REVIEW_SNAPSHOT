namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Centralized default values for <see cref="AgentChatSetting"/> fields.
    /// Every value here must stay in sync with the column defaults declared in
    /// AgentChatSetting.sql (AMAIS.WKFLO.Common.MD.DB) — this class does not change
    /// the SQL defaults, it only backfills them on the code side for creation/update
    /// paths where a caller omits a value for a NOT NULL column.
    /// </summary>
    public static class AgentChatSettingDefaults
    {
        public const string ChatWidth = "standard";
        public const string PrimaryColor = "#3b82f6";
        public const string BubbleStyle = "rounded";
        public const string FloatingPosition = "right";
        public const string LauncherStyle = "circle";
        public const bool ShowDescription = true;
        public const bool ChainOfThought = false;
        public const bool IsVoiceChatEnabled = false;
    }
}
