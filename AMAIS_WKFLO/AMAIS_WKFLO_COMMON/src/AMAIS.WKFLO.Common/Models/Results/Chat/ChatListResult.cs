namespace AMAIS.WKFLO.Common.Models.Results.Chat
{
    public class ChatListResult
    {
        public int AgentId { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
