using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Permissions
{
    /// <summary>
    /// Shared helpers for the super power user / tenant access lookups. These deliberately do NOT use
    /// <c>.Cacheable()</c>: rows are also edited directly in SQL and must apply immediately.
    /// </summary>
    public static class PermissionHelper
    {
        /// <summary>Emails are stored and compared trimmed and lower-cased.</summary>
        public static string NormaliseEmail(string email)
            => string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

        /// <param name="normalisedEmail">An email already passed through <see cref="NormaliseEmail"/>.</param>
        public static async Task<bool> IsSuperPowerUserAsync(IMasterDataDbContext db, string normalisedEmail, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(normalisedEmail))
                return false;

            return await db.SuperPowerUsers
                .AsNoTracking()
                .AnyAsync(s => s.Email == normalisedEmail && !s.IsDeleted, cancellationToken);
        }
    }
}
