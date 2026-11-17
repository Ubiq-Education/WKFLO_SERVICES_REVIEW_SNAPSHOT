namespace AMAIS.WKFLO.Common.Models.Settings
{
    /// <summary>
    /// Settings for PostgreSQL connection
    /// </summary>
    public class NpgsqlSettings
    {
        /// <summary>
        /// Connection string for PostgreSQL database
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Default timeout for commands in seconds (optional)
        /// </summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>
        /// Maximum number of retries for transient errors
        /// </summary>
        public int MaxRetries { get; set; } = 3;
    }
}