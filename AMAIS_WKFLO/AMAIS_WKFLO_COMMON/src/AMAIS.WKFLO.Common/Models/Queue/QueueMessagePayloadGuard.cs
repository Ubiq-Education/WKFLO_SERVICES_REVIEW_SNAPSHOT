using System.Buffers;
using System.Text.Json;

namespace AMAIS.WKFLO.Common.Models.Queue
{
    public static class QueueMessagePayloadGuard
    {
        /// <summary>
        /// Checks the serialized size of a WkfloQueueMessage before it's handed to
        /// DaprClient.SaveStateAsync, so an oversized payload (e.g. large image attachments)
        /// fails with a clean validation error instead of an unhandled Dapr/gRPC exception.
        /// Uses <see cref="QueueMessageJsonOptions.Default"/> (relaxed escaping) so measuring a large
        /// base64 attachment does not allocate many times its size, and only counts the bytes: nothing is
        /// kept, and with <see cref="SegmentedStringJsonConverter"/> on string properties it allocates almost nothing.
        /// </summary>
        public static bool ExceedsMaxSize<TCommand>(this WkfloQueueMessage<TCommand> data, out int payloadSizeBytes)
        {
            var options = QueueMessageJsonOptions.Default;
            var counter = new CountingBufferWriter();
            using (var writer = new Utf8JsonWriter(counter, new JsonWriterOptions
            {
                Encoder = options.Encoder,
                Indented = options.WriteIndented,
                IndentCharacter = options.IndentCharacter,
                IndentSize = options.IndentSize,
                NewLine = options.NewLine,
                MaxDepth = options.MaxDepth
            }))
            {
                JsonSerializer.Serialize(writer, data, options);
            }

            payloadSizeBytes = (int)Math.Min(counter.Count, int.MaxValue);
            return counter.Count > CommonConsts.CommonAiPubSubConsts.MaxQueueMessageSizeBytes;
        }

        /// <summary>Counts what is written and reuses one scratch buffer for it.</summary>
        private sealed class CountingBufferWriter : IBufferWriter<byte>
        {
            private byte[] _scratch = new byte[32 * 1024];

            public long Count { get; private set; }

            public void Advance(int count) => Count += count;

            public Memory<byte> GetMemory(int sizeHint = 0) => GetScratch(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0) => GetScratch(sizeHint);

            private byte[] GetScratch(int sizeHint)
            {
                if (sizeHint > _scratch.Length)
                    _scratch = new byte[sizeHint];

                return _scratch;
            }
        }
    }
}
