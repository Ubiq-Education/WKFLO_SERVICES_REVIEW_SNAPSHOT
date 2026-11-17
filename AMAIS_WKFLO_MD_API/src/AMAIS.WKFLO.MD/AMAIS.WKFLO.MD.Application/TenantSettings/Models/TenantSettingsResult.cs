using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;

namespace AMAIS.WKFLO.MD.Application.TenantSettings.Models
{
    /// <summary>
    /// The tenant's settings, or <see cref="TenantSettingDefaults"/> when the tenant has no active
    /// settings row. Returned by both GET and PUT <c>/api/v1/tenant/settings</c>.
    /// </summary>
    public class TenantSettingsResult
    {
        /// <summary>IANA time zone id, e.g. "Europe/London". Defaults to "UTC".</summary>
        public string TimeZone { get; set; }

        /// <summary>"12h" or "24h". Defaults to "24h".</summary>
        public string TimeFormat { get; set; }

        /// <summary>A whitelisted .NET date pattern, e.g. "dd/MM/yyyy". Defaults to "yyyy-MM-dd".</summary>
        public string DateFormat { get; set; }

        /// <summary>When the settings were last created or changed; null if the tenant has never saved any.</summary>
        public DateTimeOffset? LastUpdatedOn { get; set; }

        /// <param name="setting">The tenant's settings row; null or soft-deleted means "never saved".</param>
        public static TenantSettingsResult Create(TenantSetting setting)
        {
            if (setting is null || setting.IsDeleted)
            {
                return new TenantSettingsResult
                {
                    TimeZone = TenantSettingDefaults.TimeZone,
                    TimeFormat = TenantSettingDefaults.TimeFormat,
                    DateFormat = TenantSettingDefaults.DateFormat,
                    LastUpdatedOn = null
                };
            }

            return new TenantSettingsResult
            {
                TimeZone = setting.TimeZone,
                TimeFormat = setting.TimeFormat,
                DateFormat = setting.DateFormat,
                LastUpdatedOn = setting.LastUpdatedOn ?? setting.CreatedOn
            };
        }
    }
}
