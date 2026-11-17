namespace AMAIS.WKFLO.Common.MD.Domain.TenantSettings
{
    /// <summary>
    /// Values a tenant gets for a setting it has never saved. They match the column defaults on
    /// <c>dbo.TenantSetting</c> and are themselves valid, normalised values.
    /// </summary>
    public static class TenantSettingDefaults
    {
        public const string TimeZone = TimeZoneIdNormaliser.Utc;
        public const string TimeFormat = TimeFormatNormaliser.TwentyFourHour;
        public const string DateFormat = DateFormatNormaliser.IsoDate;
    }
}
