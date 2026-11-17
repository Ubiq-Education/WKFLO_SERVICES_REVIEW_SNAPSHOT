using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;

namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Models
{
    public class DxpIntegrationCreateResult : IMap
    {
        public int Id { get; set; }
        public string DxpTenantName { get; set; }
        public string Domain { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<DxpIntegration, DxpIntegrationCreateResult>();
        }
    }
}
