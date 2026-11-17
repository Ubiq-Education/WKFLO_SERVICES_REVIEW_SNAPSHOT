namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>Identifies this process in invalidation messages so it can skip its own.</summary>
    public sealed class EfCacheInstanceId(string value)
    {
        public EfCacheInstanceId() : this(Guid.NewGuid().ToString("N")) { }

        public string Value { get; } = value;
    }
}
