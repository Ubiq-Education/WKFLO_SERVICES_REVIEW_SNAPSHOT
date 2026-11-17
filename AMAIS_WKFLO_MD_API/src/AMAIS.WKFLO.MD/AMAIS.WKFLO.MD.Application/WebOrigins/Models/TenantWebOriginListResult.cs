using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.WebOrigins.Models
{
    /// <summary>
    /// Read-only view of a stored web origin, including the soft-delete flag and audit timestamps.
    /// </summary>
    public class TenantWebOriginListResult
    {
        public int Id { get; set; }
        public string WebOriginType { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? LastUpdatedOn { get; set; }

        public static TenantWebOriginListResult From(WebOrigin entity) => new()
        {
            Id = entity.Id,
            WebOriginType = entity.WebOriginType,
            Name = entity.Name,
            IsActive = entity.IsActive,
            IsDeleted = entity.IsDeleted,
            CreatedOn = entity.CreatedOn,
            LastUpdatedOn = entity.LastUpdatedOn
        };
    }
}
