using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Infrastructure.Services.PUM;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.Infrastructure.Services.PUM
{
    /// <summary>
    /// WI 7303: covers PumUsageReporter.ComputeCreditsAsync (turns an AgentAnswer's real per-call
    /// TokenUsage into PUM-reportable credits via the AIModel's configured AICredit multipliers) and
    /// ResolveReportedUserName (the reported identity - just UserContext.Email). Both are internal and
    /// exercised directly (see AssemblyInfo.cs's InternalsVisibleTo("AMAIS.WKFLO.AI.UnitTests")) rather
    /// than through the public ReportAsync, since ReportAsync ultimately calls UsageLogClient.SendLogAsync,
    /// whose members aren't virtual/mockable and which performs a real Event Hub send — see
    /// PumLogHelperTests for coverage of the Metadata dictionary ReportAsync eventually builds.
    /// </summary>
    public class PumUsageReporterTests
    {
        private const int AIModelId = 42;

        private static PumUsageReporter BuildReporter(IEnumerable<AICredit> aiCredits)
        {
            var dbSet = new List<AICredit>(aiCredits).BuildMockDbSet();
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AICredits).Returns(dbSet.Object);

            return BuildReporter(db);
        }

        private static PumUsageReporter BuildReporter(Mock<IMasterDataDbContext> db)
        {
            // UsageLogClient/TenantContext/ChatContext/UserContext/IDateTimeProvider are irrelevant to
            // ComputeCreditsAsync (it never touches them), so they're left null/default here.
            return new PumUsageReporter(null, new TenantContext(), new ChatContext(), new UserContext(), Mock.Of<IDateTimeProvider>(), db.Object, NullLogger<PumUsageReporter>.Instance);
        }

        private static AgentAnswer BuildAgentAnswer(int? aiModelId, long inputTokens, long outputTokens) => new()
        {
            AIModelId = aiModelId,
            TokenUsage = new AITokenUsage { InputTokenCount = inputTokens, OutputTokenCount = outputTokens }
        };

        [Fact]
        public async Task ComputeCreditsAsync_MatchingAICreditFound_ComputesFromActualTokenUsageNotReferenceTokens()
        {
            // AICredit's own TokensIn/TokensOut (the reference example values backing the unrelated
            // SQL-computed Credits column) are deliberately set to values that would produce a different
            // result if they were mistakenly used instead of the AgentAnswer's actual TokenUsage.
            var aiCredit = new AICredit
            {
                AIModelId = AIModelId,
                TokensIn = 999_999,
                TokensOut = 999_999,
                MultiplierIn = 2m,
                MultiplierOut = 4m
            };
            var reporter = BuildReporter([aiCredit]);
            var agentAnswer = BuildAgentAnswer(AIModelId, inputTokens: 1000, outputTokens: 500);

            var credits = await reporter.ComputeCreditsAsync(agentAnswer);

            // (1000 * 2 / 1000) + (500 * 4 / 1000) = 2 + 2 = 4
            Assert.Equal(4m, credits);
        }

        [Fact]
        public async Task ComputeCreditsAsync_NoAICreditRowForAIModel_ReturnsNullWithoutThrowing()
        {
            var reporter = BuildReporter([]);
            var agentAnswer = BuildAgentAnswer(AIModelId, inputTokens: 1000, outputTokens: 500);

            var credits = await reporter.ComputeCreditsAsync(agentAnswer);

            Assert.Null(credits);
        }

        [Fact]
        public async Task ComputeCreditsAsync_AIModelIdNull_ReturnsNullWithoutThrowing()
        {
            var aiCredit = new AICredit { AIModelId = AIModelId, MultiplierIn = 2m, MultiplierOut = 4m };
            var reporter = BuildReporter([aiCredit]);
            var agentAnswer = BuildAgentAnswer(aiModelId: null, inputTokens: 1000, outputTokens: 500);

            var credits = await reporter.ComputeCreditsAsync(agentAnswer);

            Assert.Null(credits);
        }

        [Fact]
        public async Task ComputeCreditsAsync_SoftDeletedAICredit_ReturnsNullWithoutThrowing()
        {
            // A soft-deleted AICredit row must not be picked up, same as every other soft-deletable entity
            // in this codebase — matching the !c.IsDeleted filter used in GetAICreditByAIModelIdQueryHandler's
            // sibling query shape.
            var aiCredit = new AICredit { AIModelId = AIModelId, MultiplierIn = 2m, MultiplierOut = 4m, IsDeleted = true };
            var reporter = BuildReporter([aiCredit]);
            var agentAnswer = BuildAgentAnswer(AIModelId, inputTokens: 1000, outputTokens: 500);

            var credits = await reporter.ComputeCreditsAsync(agentAnswer);

            Assert.Null(credits);
        }

        [Fact]
        public async Task ComputeCreditsAsync_DbThrows_ReturnsNullWithoutThrowing()
        {
            // This runs on the agent-response critical path (published before chat history is
            // persisted/returned to the caller), so a transient DB failure here must never fail the
            // whole chat turn just to report usage - same "never crash" contract as the other null cases.
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AICredits).Throws(new InvalidOperationException("simulated DB failure"));
            var reporter = BuildReporter(db);
            var agentAnswer = BuildAgentAnswer(AIModelId, inputTokens: 1000, outputTokens: 500);

            var credits = await reporter.ComputeCreditsAsync(agentAnswer);

            Assert.Null(credits);
        }

        // Regression test (Copilot PR comment): a revert back to the old GivenName/Surname concatenation
        // (with UserName as its fallback) would not fail today without this - ReportAsync itself can't be
        // exercised directly (see class remarks), so this asserts on the extracted selection instead.
        [Fact]
        public void ResolveReportedUserName_ReturnsUserContextEmail()
        {
            var userContext = new UserContext
            {
                Email = "jane.doe@example.com",
                UserName = "jdoe",
                GivenName = "Jane",
                Surname = "Doe"
            };

            var result = PumUsageReporter.ResolveReportedUserName(userContext);

            Assert.Equal("jane.doe@example.com", result);
        }

        [Fact]
        public void ResolveReportedUserName_NullUserContext_ReturnsNullWithoutThrowing()
        {
            var result = PumUsageReporter.ResolveReportedUserName(null);

            Assert.Null(result);
        }
    }
}
