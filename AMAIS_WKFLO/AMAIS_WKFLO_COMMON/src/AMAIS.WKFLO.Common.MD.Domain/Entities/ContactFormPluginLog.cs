using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class ContactFormPluginLog : Entity<int>
    {
        public int TenantId { get; set; }
        public int AgentId { get; set; }
        public string SubjectId { get; set; }
        public string SubjectIdSource { get; set; }
        public string Action { get; set; }

        public Tenant Tenant { get; set; }
        public Agent Agent { get; set; }
    }
}
