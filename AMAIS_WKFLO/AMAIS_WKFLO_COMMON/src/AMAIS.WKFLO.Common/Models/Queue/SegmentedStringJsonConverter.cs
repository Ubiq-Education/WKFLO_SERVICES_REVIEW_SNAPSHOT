using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.Models.Queue
{
    /// <summary>
    /// Writes a long string (e.g. a base64 file attachment) in 8K-character segments. Same JSON as the default
    /// string handling, but <see cref="Utf8JsonWriter"/> only needs room for one segment at a time instead of
    /// 3x the whole string (46 MB, a 64 MiB pooled buffer, for an 11 MB attachment). Together with
    /// <see cref="QueueMessagePayloadGuard"/>'s counting writer, measuring a message no longer buffers it.
    /// Applied to string properties by <see cref="QueueMessageJsonOptions"/>.
    /// </summary>
    public sealed class SegmentedStringJsonConverter : JsonConverter<string>
    {
        private const int SegmentLength = 8192;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.Length <= SegmentLength)
            {
                writer.WriteStringValue(value);
                return;
            }

            var remaining = value.AsSpan();
            while (remaining.Length > SegmentLength)
            {
                // Never split a surrogate pair across segments.
                var length = char.IsHighSurrogate(remaining[SegmentLength - 1]) ? SegmentLength - 1 : SegmentLength;
                writer.WriteStringValueSegment(remaining[..length], isFinalSegment: false);
                remaining = remaining[length..];
            }

            writer.WriteStringValueSegment(remaining, isFinalSegment: true);
        }
    }
}
