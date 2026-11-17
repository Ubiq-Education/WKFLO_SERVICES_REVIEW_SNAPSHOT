using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Text.Json;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Models
{
    /// <summary>
    /// US 7974: the MD chat history returns each message's persisted timestamp in UTC, and null (never 0001-01-01)
    /// when a message was stored without one.
    /// </summary>
    public class TenantAgentChatMessageResultTimestampTests
    {
        private static readonly DateTimeOffset Utc = new(2026, 10, 22, 9, 30, 15, TimeSpan.Zero);

        private readonly IWkfloMapper _mapper = WkfloMapperExtensions.CreateMapper();

        private static ChatMessageContent Message(object timestamp) =>
            new(AuthorRole.User, "Hello", metadata: timestamp == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object> { [CommonMetadataConsts.Timestamp] = timestamp });

        [Fact]
        public void StoredTimestampWithAnotherOffset_IsReturnedInUtc()
        {
            var result = _mapper.Map<TenantAgentChatMessageResult>(Message(JsonDocument.Parse("\"2026-10-22T11:30:15+02:00\"").RootElement));

            Assert.Equal(Utc, result.Timestamp);
            Assert.Equal(TimeSpan.Zero, result.Timestamp!.Value.Offset);
        }

        [Fact]
        public void MissingTimestamp_IsNull()
        {
            Assert.Null(_mapper.Map<TenantAgentChatMessageResult>(Message(null)).Timestamp);
        }

        [Fact]
        public void DefaultTimestamp_IsNullNotMinValue()
        {
            Assert.Null(_mapper.Map<TenantAgentChatMessageResult>(Message(default(DateTimeOffset))).Timestamp);
        }

        [Fact]
        public void BoxedDefaultDateTime_IsNull_WithoutThrowingOnAHostAheadOfUtc()
        {
            Assert.Null(_mapper.Map<TenantAgentChatMessageResult>(Message(default(DateTime))).Timestamp);
        }
    }
}
