using AMAIS.WKFLO.Common.Interfaces.Providers;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    public class DateTimeProvider : IDateTimeProvider
    {
        public DateTime DateTimeUtcNow() => DateTime.UtcNow;
        public DateTimeOffset DateTimeOffsetUtcNow() => DateTimeOffset.UtcNow;
        public DateTimeOffset DateTimeOffsetMinValue() => DateTimeOffset.MinValue;
    }
}
