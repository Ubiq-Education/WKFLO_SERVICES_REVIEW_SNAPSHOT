using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.Audit.Models
{
    public class AuditLogEntryResult : IMap
    {
        public int Id { get; set; }
        public string EntityType { get; set; }
        public string EntityId { get; set; }
        public string Action { get; set; }
        public string ActorUserGuid { get; set; }
        public string ActorUserName { get; set; }
        public string ActorUserEmail { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Changes { get; set; }
        public string CorrelationId { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AuditLogEntry, AuditLogEntryResult>();
        }
    }
}
