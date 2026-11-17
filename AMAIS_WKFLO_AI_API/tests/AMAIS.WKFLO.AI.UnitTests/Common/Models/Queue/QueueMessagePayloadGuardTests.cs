using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Models.Queue;

namespace AMAIS.WKFLO.AI.UnitTests.Common.Models.Queue
{
    public class QueueMessagePayloadGuardTests
    {
        private readonly ITestOutputHelper _output;

        public QueueMessagePayloadGuardTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private class AttachmentCommand
        {
            public List<FileAttachment> Files { get; set; }
        }

        private static AttachmentCommand WithFile(string fileData) =>
            new() { Files = [new FileAttachment { FileName = "report.pdf", FileData = fileData }] };

        private class PlainStringValue
        {
            public string Value { get; set; }
        }

        private class SegmentedStringValue
        {
            [JsonConverter(typeof(SegmentedStringJsonConverter))]
            public string Value { get; set; }
        }

        // Characters the encoders escape, non-ASCII text, and a surrogate pair straddling the 8192-char segment end.
        private static readonly string AwkwardLongString =
            new string('a', 8191) + "\uD83D\uDE00" + "+/\"\\<>&\n é" + new string('b', 20_000);

        private static readonly JsonSerializerOptions RelaxedWithoutSegments =
            new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private static WkfloQueueMessage<AttachmentCommand> BuildAttachmentMessage(int fileBytes)
        {
            var bytes = new byte[fileBytes];
            new Random(42).NextBytes(bytes);
            return new WkfloQueueMessage<AttachmentCommand>
            {
                Command = WithFile("data:application/pdf;base64," + Convert.ToBase64String(bytes)),
                InstanceId = "instance-attachment"
            };
        }

        private class TestCommand
        {
            public string Text { get; set; }
        }

        [Fact]
        public void ExceedsMaxSize_ReturnsFalse_ForSmallPayload()
        {
            var data = new WkfloQueueMessage<TestCommand>
            {
                Command = new TestCommand { Text = "hello" },
                InstanceId = "instance-1"
            };

            var exceeds = data.ExceedsMaxSize(out var payloadSizeBytes);

            Assert.False(exceeds);
            Assert.True(payloadSizeBytes > 0);
        }

        [Fact]
        public void ExceedsMaxSize_ReturnsTrue_ForPayloadOverLimit()
        {
            var data = new WkfloQueueMessage<TestCommand>
            {
                Command = new TestCommand { Text = new string('a', 19_000_000) },
                InstanceId = "instance-2"
            };

            var exceeds = data.ExceedsMaxSize(out var payloadSizeBytes);

            Assert.True(exceeds);
            Assert.True(payloadSizeBytes > 18_000_000);
        }

        [Fact]
        public void ExceedsMaxSize_DoesNotAmplifyAllocations_ForLargeBase64Attachment()
        {
            var data = BuildAttachmentMessage(11 * 1024 * 1024);
            var jsonSize = JsonSerializer.SerializeToUtf8Bytes(data, QueueMessageJsonOptions.Default).Length;

            // Warm up so one-time metadata/JIT allocations are not counted.
            new WkfloQueueMessage<AttachmentCommand> { Command = WithFile("x") }.ExceedsMaxSize(out _);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var exceeds = data.ExceedsMaxSize(out _);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            _output.WriteLine($"JSON size: {jsonSize:N0} bytes, allocated: {allocated:N0} bytes ({(double)allocated / jsonSize:F2}x)");

            // Only counted, never buffered: the attachment is written in segments into one small scratch buffer.
            // Before: ~80 MB on the first call, with a 64 MiB buffer kept in the shared ArrayPool afterwards.
            Assert.False(exceeds);
            Assert.True(allocated < 1024 * 1024, $"Allocated {allocated:N0} bytes for a {jsonSize:N0}-byte payload");
        }

        [Fact]
        public void ExceedsMaxSize_ReportsSizeOfQueueMessageJsonOptionsOutput()
        {
            var data = BuildAttachmentMessage(1024 * 1024);

            data.ExceedsMaxSize(out var payloadSizeBytes);

            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(data, QueueMessageJsonOptions.Default).Length, payloadSizeBytes);
        }

        [Fact]
        public void QueueMessageJsonOptions_RoundTripsAttachment_AndDoesNotEscapePlus()
        {
            var data = BuildAttachmentMessage(64 * 1024);

            var json = JsonSerializer.SerializeToUtf8Bytes(data, QueueMessageJsonOptions.Default);
            var roundTripped = JsonSerializer.Deserialize<WkfloQueueMessage<AttachmentCommand>>(json, QueueMessageJsonOptions.Default);

            Assert.Equal(data.Command.Files[0].FileData, roundTripped.Command.Files[0].FileData);
            Assert.DoesNotContain("\\u002B", System.Text.Encoding.UTF8.GetString(json));
        }

        [Fact]
        public void QueueMessageJsonOptions_IsReadOnly_AndMatchesWebDefaults()
        {
            Assert.True(QueueMessageJsonOptions.Default.IsReadOnly);
            Assert.Equal(JsonNamingPolicy.CamelCase, QueueMessageJsonOptions.Default.PropertyNamingPolicy);
            Assert.True(QueueMessageJsonOptions.Default.PropertyNameCaseInsensitive);
            Assert.Equal(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString, QueueMessageJsonOptions.Default.NumberHandling);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SegmentedStringJsonConverter_WritesSameJsonAsPlainString(bool relaxedEncoder)
        {
            var options = relaxedEncoder ? RelaxedWithoutSegments : new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var segmented = JsonSerializer.Serialize(new SegmentedStringValue { Value = AwkwardLongString }, options);
            var plain = JsonSerializer.Serialize(new PlainStringValue { Value = AwkwardLongString }, options);

            Assert.Equal(plain, segmented);
            Assert.Equal(AwkwardLongString, JsonSerializer.Deserialize<SegmentedStringValue>(segmented, options).Value);
        }

        [Fact]
        public void QueueMessageJsonOptions_WritesSameJsonAsWithoutSegmenting()
        {
            var data = new WkfloQueueMessage<AttachmentCommand> { Command = WithFile(AwkwardLongString), InstanceId = "instance-3" };

            var json = JsonSerializer.Serialize(data, QueueMessageJsonOptions.Default);

            Assert.Equal(JsonSerializer.Serialize(data, RelaxedWithoutSegments), json);
            Assert.Equal(AwkwardLongString,
                JsonSerializer.Deserialize<WkfloQueueMessage<AttachmentCommand>>(json, QueueMessageJsonOptions.Default).Command.Files[0].FileData);
        }

        [Fact]
        public void FileAttachment_SchemaForOtherSerializers_KeepsFileDataAsString()
        {
            // The segmenting lives in QueueMessageJsonOptions only; an attribute on the model would turn fileData
            // into an untyped schema in the OpenAPI document.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

            var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(FileAttachment));

            Assert.Contains("string", schema["properties"]!["fileData"]!["type"]!.ToJsonString());
        }

        [Fact]
        public void SegmentedStringJsonConverter_NullFileData_StaysNull()
        {
            var json = JsonSerializer.Serialize(new FileAttachment(), QueueMessageJsonOptions.Default);

            Assert.Contains("\"fileData\":null", json);
            Assert.Null(JsonSerializer.Deserialize<FileAttachment>(json, QueueMessageJsonOptions.Default).FileData);
        }
    }
}
