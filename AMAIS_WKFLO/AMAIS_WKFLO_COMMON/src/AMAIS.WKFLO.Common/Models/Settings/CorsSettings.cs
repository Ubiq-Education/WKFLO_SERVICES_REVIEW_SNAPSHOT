namespace AMAIS.WKFLO.Common.Models.Settings
{
    public class CorsSettings
    {
        public string PolicyName { get; set; }

        /// <summary>
        /// Optional. Allow any origin (development only). Credentials stay allowed through an origin predicate.
        /// </summary>
        public bool AllowAnyOrigin { get; set; }

        /// <summary>
        /// Optional. Maximum age, in seconds, of the cached allowed origins before a reload. Defaults to 300.
        /// </summary>
        public int? OriginsRefreshSeconds { get; set; }

        /// <summary>
        /// Optional. Minimum seconds between reloads triggered by an unknown origin. Defaults to 30.
        /// </summary>
        public int? OriginsMissRefreshSeconds { get; set; }
    }
}
