using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Models
{
    /// <summary>
    /// US 7974: how the chat shows message dates and times for the agent's tenant. Returned with the chat
    /// configuration the widget, share page and test chat already load, so none of them needs an authenticated call
    /// to the tenant settings. Only these three display settings are exposed.
    /// </summary>
    public class TenantDisplaySettingsResult
    {
        /// <summary>IANA time zone id, e.g. "Europe/London". Defaults to "UTC".</summary>
        public string TimeZone { get; set; }

        /// <summary>"12h" or "24h". Defaults to "24h".</summary>
        public string TimeFormat { get; set; }

        /// <summary>A whitelisted .NET date pattern, e.g. "dd/MM/yyyy". Defaults to "yyyy-MM-dd".</summary>
        public string DateFormat { get; set; }

        /// <param name="setting">The tenant's settings row; null or soft-deleted means "never saved" and gives <see cref="TenantSettingDefaults"/>.</param>
        public static TenantDisplaySettingsResult Create(TenantSetting setting)
        {
            var saved = setting is { IsDeleted: false } ? setting : null;

            return new TenantDisplaySettingsResult
            {
                TimeZone = OrDefault(saved?.TimeZone, TenantSettingDefaults.TimeZone),
                TimeFormat = OrDefault(saved?.TimeFormat, TenantSettingDefaults.TimeFormat),
                DateFormat = OrDefault(saved?.DateFormat, TenantSettingDefaults.DateFormat)
            };
        }

        private static string OrDefault(string value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
