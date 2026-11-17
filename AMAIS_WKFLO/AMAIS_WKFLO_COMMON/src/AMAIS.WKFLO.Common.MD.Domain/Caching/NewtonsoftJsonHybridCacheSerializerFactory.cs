using System.Buffers;
using System.Text;
using Microsoft.Extensions.Caching.Hybrid;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.MD.Domain.Caching
{
    /// <summary>
    /// HybridCache serializer for the EF second-level cache payloads (<c>EFCachedData</c> holds untyped column values,
    /// which System.Text.Json cannot round-trip), so Newtonsoft with embedded type names is required.
    /// </summary>
    public sealed class NewtonsoftJsonHybridCacheSerializer<T> : IHybridCacheSerializer<T>
    {
        internal static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            SerializationBinder = new EfCacheSerializationBinder(),
            NullValueHandling = NullValueHandling.Ignore,
            // Values are untyped objects: keep dates as the ISO text they are written as (the EF reader parses them by column type)
            // and decimals as decimals instead of doubles, which would lose precision.
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
        };

        public T Deserialize(ReadOnlySequence<byte> source)
        {
            var json = Encoding.UTF8.GetString(source);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public void Serialize(T value, IBufferWriter<byte> target)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Settings));
            target.Write(bytes);
        }
    }

    public sealed class NewtonsoftJsonHybridCacheSerializerFactory : IHybridCacheSerializerFactory
    {
        public bool TryCreateSerializer<T>(out IHybridCacheSerializer<T> serializer)
        {
            serializer = new NewtonsoftJsonHybridCacheSerializer<T>();
            return true;
        }
    }
}
