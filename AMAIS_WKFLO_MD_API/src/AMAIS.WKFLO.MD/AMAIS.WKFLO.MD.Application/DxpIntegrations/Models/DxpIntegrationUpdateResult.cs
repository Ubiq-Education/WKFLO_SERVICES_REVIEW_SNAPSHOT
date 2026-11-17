namespace AMAIS.WKFLO.MD.Application.DxpIntegrations.Models
{
    public class DxpIntegrationUpdateResult
    {
        public int Id { get; set; }
        public string DxpTenantName { get; set; }
        public string Domain { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }
    }
}
