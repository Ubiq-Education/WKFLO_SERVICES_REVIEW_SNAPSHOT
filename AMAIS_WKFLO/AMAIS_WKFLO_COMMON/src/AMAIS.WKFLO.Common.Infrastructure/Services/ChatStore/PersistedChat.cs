namespace AMAIS.WKFLO.Common.Infrastructure.Services.ChatStore
{
    public class PersistedChat
    {
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string SerializedChatHistory { get; set; } = string.Empty;
        public Dictionary<string, string> MessageRatings { get; set; } = new();
    }
}
