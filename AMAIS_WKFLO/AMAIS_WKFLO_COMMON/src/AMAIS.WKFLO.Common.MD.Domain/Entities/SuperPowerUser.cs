using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// An email address allowed to see every tenant and every marketplace action in the WKFLO UI.
    /// Rows are never hard-deleted: re-adding an email reactivates the existing row (the unique index on
    /// <see cref="Email"/> includes soft-deleted rows). The email is stored trimmed and lower-cased.
    /// There is deliberately no query filter on <see cref="Entity{T}.IsDeleted"/> so reactivation can find the row.
    /// </summary>
    public class SuperPowerUser : Entity<int>
    {
        public string Email { get; set; }

        /// <summary>Email of the super power user who added it; null for rows inserted manually in SQL.</summary>
        public string CreatedBy { get; set; }
    }
}
