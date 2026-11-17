using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.TenantAccesses.Models
{
    public class TenantAccessResult
    {
        public int Id { get; set; }
        public string TenantGuid { get; set; }
        public string TenantName { get; set; }
        public string Email { get; set; }
        public string CreatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }

        public static TenantAccessResult From(TenantAccess entity, Tenant tenant) => new()
        {
            Id = entity.Id,
            TenantGuid = tenant.Guid.ToString(),
            TenantName = tenant.Name,
            Email = entity.Email,
            CreatedBy = entity.CreatedBy,
            CreatedOn = entity.CreatedOn
        };
    }
}
