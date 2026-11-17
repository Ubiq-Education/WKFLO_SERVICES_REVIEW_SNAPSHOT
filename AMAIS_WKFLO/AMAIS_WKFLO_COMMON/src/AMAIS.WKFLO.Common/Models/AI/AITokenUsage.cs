namespace AMAIS.WKFLO.Common.Models.AI
{
    public class AITokenUsage
    {
        public long InputTokenCount { get; set; }

        public long OutputTokenCount { get; set; }

        public long TotalTokenCount => InputTokenCount + OutputTokenCount;
    }
}
