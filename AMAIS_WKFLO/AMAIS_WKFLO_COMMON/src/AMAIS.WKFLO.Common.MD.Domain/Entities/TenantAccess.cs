using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// Grants an email address access to one tenant in the WKFLO UI tenant switcher. Unique on
    /// (<see cref="TenantId"/>, <see cref="Email"/>) including soft-deleted rows, so re-adding reactivates the row.
    /// The email is stored trimmed and lower-cased. No query filter on IsDeleted, so reactivation can find the row.
    /// </summary>
    public class TenantAccess : Entity<int>
    {
        public int TenantId { get; set; }
        public virtual Tenant Tenant { get; set; }

        public string Email { get; set; }

        /// <summary>Email of the super power user who added it; null for rows inserted manually in SQL.</summary>
        public string CreatedBy { get; set; }
    }
}
