using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AMAIS.WKFLO.Common.Models.Queue
{
    /// <summary>
    /// Single shared <see cref="JsonSerializerOptions"/> for queued WKFLO messages. It is used both by
    /// <see cref="QueueMessagePayloadGuard"/> (to measure the payload) and by the AI API's DaprClient
    /// (to write it to the state store / pub-sub), so the guard measures exactly what Dapr writes.
    ///
    /// Same settings as Dapr.Client's own defaults (<see cref="JsonSerializerDefaults.Web"/>: camelCase,
    /// case-insensitive reads, numbers readable from strings) apart from the encoder.
    ///
    /// Why relaxed escaping: the default encoder escapes '+' (very common in base64), which pushes
    /// System.Text.Json's writer onto its escaping path that rents a scratch buffer of ~6x the string
    /// length. For an 11 MB attachment that allocated ~352 MB per serialization versus ~15 MB without it,
    /// enough to throw OutOfMemoryException in a 0.5 GiB container.
    ///
    /// Why it is safe: this JSON only goes to the Redis state store and pub/sub, never into HTML.
    /// The output is still ordinary valid JSON, so older and newer replicas read each other's messages.
    ///
    /// Every string property is written with <see cref="SegmentedStringJsonConverter"/> (same JSON, less
    /// buffering for a large attachment). It is added here rather than as an attribute on the model so that
    /// MVC, the OpenAPI document and other serializers keep the plain string handling and schema.
    /// </summary>
    public static class QueueMessageJsonOptions
    {
        public static JsonSerializerOptions Default { get; } = CreateDefault();

        private static readonly SegmentedStringJsonConverter SegmentedString = new();

        private static JsonSerializerOptions CreateDefault()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SegmentStringProperties } }
            };

            // Shared instance: freeze it so nobody mutates it after first use.
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }

        private static void SegmentStringProperties(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (var property in typeInfo.Properties)
            {
                if (property.PropertyType == typeof(string) && property.CustomConverter == null)
                    property.CustomConverter = SegmentedString;
            }
        }
    }
}
