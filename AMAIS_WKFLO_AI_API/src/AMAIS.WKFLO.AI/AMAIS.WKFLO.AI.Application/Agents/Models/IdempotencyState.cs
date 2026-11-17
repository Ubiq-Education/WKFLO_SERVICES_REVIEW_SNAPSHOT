namespace AMAIS.WKFLO.AI.Application.Agents.Models
{
    public class IdempotencyState
    {
        public string Status { get; set; } // processing, done, failed
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
