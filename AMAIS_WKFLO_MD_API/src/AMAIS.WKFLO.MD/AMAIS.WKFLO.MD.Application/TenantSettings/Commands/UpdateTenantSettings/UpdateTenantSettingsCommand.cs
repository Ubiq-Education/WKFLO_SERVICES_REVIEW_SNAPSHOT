using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;

namespace AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings
{
    /// <summary>
    /// Partial update of the current tenant's settings. A null property means "leave unchanged".
    /// </summary>
    public class UpdateTenantSettingsCommand : IWkfloRequest<Result>
    {
        /// <summary>IANA id (e.g. "Europe/London") or Windows id (normalised to IANA). Raw offsets are rejected.</summary>
        public string TimeZone { get; set; }

        /// <summary>"12h" or "24h" (case-insensitive).</summary>
        public string TimeFormat { get; set; }

        /// <summary>
        /// A whitelisted .NET date pattern, e.g. "dd/MM/yyyy" (case-sensitive; see DateFormatNormaliser.AllowedFormats).
        /// </summary>
        public string DateFormat { get; set; }
    }
}
