namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// Tags to invalidate and the process that already did so. <see cref="AllTag"/> clears every entry.
    /// </summary>
    public sealed record EfCacheInvalidationMessage(string SenderId, string[] Tags)
    {
        // HybridCache treats the "*" tag as "everything".
        public const string AllTag = "*";
    }
}
