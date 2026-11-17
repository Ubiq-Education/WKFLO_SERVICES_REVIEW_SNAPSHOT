using AMAIS.WKFLO.AI.Application.Agents.Models;
using AMAIS.WKFLO.Common.Mappings;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Models
{
    /// <summary>
    /// US 7974: the chat history endpoints return each message's persisted timestamp in UTC, and null (never
    /// 0001-01-01) when a message was stored without one.
    /// </summary>
    public class ChatMessageResultTimestampTests
    {
        private static readonly DateTimeOffset Utc = new(2026, 10, 22, 9, 30, 15, TimeSpan.Zero);

        private readonly IWkfloMapper _mapper = WkfloMapperExtensions.CreateMapper();

        private static ChatMessageContent Message(object timestamp) =>
            new(AuthorRole.Assistant, "Hello", metadata: timestamp == null
                ? new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = "m-1" }
                : new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = "m-1", [CommonMetadataConsts.Timestamp] = timestamp });

        public static TheoryData<object> StoredTimestamps => new()
        {
            Utc,
            Utc.ToOffset(TimeSpan.FromHours(2)),
            "2026-10-22T09:30:15+00:00",
            // As read back from the chat blob.
            JsonDocument.Parse("\"2026-10-22T11:30:15+02:00\"").RootElement
        };

        public static TheoryData<object> MissingTimestamps => new()
        {
            null,
            default(DateTimeOffset),
            // Boxed default(DateTime): must not throw on a host ahead of UTC.
            default(DateTime),
            "not a date"
        };

        [Theory]
        [MemberData(nameof(StoredTimestamps))]
        public void TenantResult_ReturnsTheStoredTimestampInUtc(object stored)
        {
            var result = _mapper.Map<TenantAgentChatMessageResult>(Message(stored));

            Assert.Equal(Utc, result.Timestamp);
            Assert.Equal(TimeSpan.Zero, result.Timestamp!.Value.Offset);
        }

        [Theory]
        [MemberData(nameof(StoredTimestamps))]
        public void PublicResult_ReturnsTheStoredTimestampInUtc(object stored)
        {
            var result = _mapper.Map<PublicAgentChatMessageResult>(Message(stored));

            Assert.Equal(Utc, result.Timestamp);
            Assert.Equal(TimeSpan.Zero, result.Timestamp!.Value.Offset);
        }

        [Theory]
        [MemberData(nameof(MissingTimestamps))]
        public void TenantResult_WithoutATimestamp_ReturnsNull(object stored)
        {
            Assert.Null(_mapper.Map<TenantAgentChatMessageResult>(Message(stored)).Timestamp);
        }

        [Theory]
        [MemberData(nameof(MissingTimestamps))]
        public void PublicResult_WithoutATimestamp_ReturnsNull(object stored)
        {
            Assert.Null(_mapper.Map<PublicAgentChatMessageResult>(Message(stored)).Timestamp);
        }

        [Fact]
        public void PublicResult_WithoutATimestamp_SerializesNull()
        {
            var json = JsonSerializer.Serialize(_mapper.Map<PublicAgentChatMessageResult>(Message(null)), new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Contains("\"timestamp\":null", json);
            Assert.DoesNotContain("0001-01-01", json);
        }
    }
}
