namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IDateTimeProvider
    {
        DateTime DateTimeUtcNow();
        DateTimeOffset DateTimeOffsetUtcNow();
        DateTimeOffset DateTimeOffsetMinValue();
    }
}
