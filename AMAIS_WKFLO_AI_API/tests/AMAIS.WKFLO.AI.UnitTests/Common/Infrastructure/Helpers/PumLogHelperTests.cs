using AMAIS.WKFLO.Common.Infrastructure.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models.AI;
using Moq;
using System;
using System.Collections.Generic;

namespace AMAIS.WKFLO.AI.UnitTests.Common.Infrastructure.Helpers
{
    /// <summary>
    /// WI 7303: covers PumLogHelper.BuildDataSample, the piece extracted from the private ReportUsageAsync
    /// overload specifically so the resulting UsageLogDataSample.Metadata can be asserted directly here
    /// without going through UsageLogClient.SendLogAsync (whose members aren't virtual/mockable and which
    /// performs a real Event Hub send). BuildDataSample is internal — see AssemblyInfo.cs's
    /// InternalsVisibleTo("AMAIS.WKFLO.AI.UnitTests").
    /// </summary>
    public class PumLogHelperTests
    {
        private static readonly IDateTimeProvider DateTimeProvider = Mock.Of<IDateTimeProvider>(p => p.DateTimeUtcNow() == new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        [Fact]
        public void BuildDataSample_AIModelIdAndCreditsProvided_MetadataContainsBothWithCorrectTypes()
        {
            var tokenUsage = new AITokenUsage { InputTokenCount = 100, OutputTokenCount = 50 };

            var sample = PumLogHelper.BuildDataSample(
                tenantName: "tenant-1", sessionId: "session-1", userName: "Jane Doe", userGuid: "user-guid",
                tokenUsage: tokenUsage, agentType: "Support", agentTags: null, agentId: 7,
                aiModelId: 42, credits: 3.5m, dateTimeProvider: DateTimeProvider);

            // AIModelId is stringified, matching the existing AgentId convention.
            Assert.Equal("42", sample.Metadata["AIModelId"]);

            // Credits is kept as a raw nullable decimal (NOT stringified), unlike the other string-typed
            // metadata fields, so Elasticsearch can Sum() it later.
            Assert.Equal(3.5m, sample.Metadata["Credits"]);
            Assert.IsType<decimal>(sample.Metadata["Credits"]);
        }

        [Fact]
        public void BuildDataSample_AIModelIdAndCreditsNull_MetadataContainsNullsWithoutThrowing()
        {
            var tokenUsage = new AITokenUsage { InputTokenCount = 100, OutputTokenCount = 50 };

            var sample = PumLogHelper.BuildDataSample(
                tenantName: "tenant-1", sessionId: "session-1", userName: "Jane Doe", userGuid: "user-guid",
                tokenUsage: tokenUsage, agentType: "ImageGen", agentTags: null, agentId: null,
                aiModelId: null, credits: null, dateTimeProvider: DateTimeProvider);

            Assert.Null(sample.Metadata["AIModelId"]);
            Assert.Null(sample.Metadata["Credits"]);
        }

        [Fact]
        public void BuildDataSample_AlwaysIncludesExistingTokenUsageMetadata()
        {
            // Regression guard: extending Metadata with AIModelId/Credits (WI 7303) must not disturb the
            // pre-existing fields (WI 7365 and earlier).
            var tokenUsage = new AITokenUsage { InputTokenCount = 100, OutputTokenCount = 50 };

            var sample = PumLogHelper.BuildDataSample(
                tenantName: "tenant-1", sessionId: "session-1", userName: "Jane Doe", userGuid: "user-guid",
                tokenUsage: tokenUsage, agentType: "Support", agentTags: ["Chatbot"], agentId: 7,
                aiModelId: 42, credits: 3.5m, dateTimeProvider: DateTimeProvider);

            Assert.Equal("100", sample.Metadata["InputTokenCount"]);
            Assert.Equal("50", sample.Metadata["OutputTokenCount"]);
            Assert.Equal("7", sample.Metadata["AgentId"]);
            Assert.Equal(new List<string> { "Chatbot" }, sample.Metadata["AgentTags"]);
        }
    }
}
