using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using AMAIS.WKFLO.Common.Models.Entities;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    /// <summary>
    /// A tenant's settings: one row per tenant (unique on TenantId, including soft-deleted rows), one
    /// column per setting. Values are stored already normalised (see <see cref="TimeZoneIdNormaliser"/>,
    /// <see cref="TimeFormatNormaliser"/> and <see cref="DateFormatNormaliser"/>); a tenant without a row gets <see cref="TenantSettingDefaults"/>.
    /// </summary>
    public class TenantSetting : Entity<int>
    {
        public int TenantId { get; set; }
        public virtual Tenant Tenant { get; set; }

        /// <summary>IANA time zone id, e.g. "Europe/London".</summary>
        public string TimeZone { get; set; } = TenantSettingDefaults.TimeZone;

        /// <summary>"12h" or "24h".</summary>
        public string TimeFormat { get; set; } = TenantSettingDefaults.TimeFormat;

        /// <summary>A whitelisted .NET date pattern (see <see cref="DateFormatNormaliser"/>), e.g. "dd/MM/yyyy".</summary>
        public string DateFormat { get; set; } = TenantSettingDefaults.DateFormat;
    }
}
