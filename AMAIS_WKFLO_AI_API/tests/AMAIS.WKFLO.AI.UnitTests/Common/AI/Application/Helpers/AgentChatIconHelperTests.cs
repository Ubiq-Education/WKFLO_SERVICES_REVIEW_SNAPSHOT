using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.AI.Application.Helpers
{
    public class AgentChatIconHelperTests
    {
        private const int AgentId = 5;

        private static Mock<IMasterDataDbContext> BuildDb(IEnumerable<AgentChatSetting> rows)
        {
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>(rows).BuildMockDbSet().Object);
            return db;
        }

        [Fact]
        public async Task GetChatbotIconUrlAsync_NoRootContext_PrefersWidgetRowTieBrokenByRecency()
        {
            // rootIsWidget == null means this agent IS the root resolving its own icon directly — no
            // hand-off context to match, so behavior is unchanged from before subagent propagation
            // existed: prefer the IsWidget row, tie-broken by most-recently-updated.
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = false, ChatbotIconPath = "https://cdn.test/non-widget.png", CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new AgentChatSetting { Id = 2, AgentId = AgentId, IsWidget = true, ChatbotIconPath = "https://cdn.test/widget.png", CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
            });

            var result = await AgentChatIconHelper.GetChatbotIconUrlAsync(db.Object, AgentId, rootIsWidget: null, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("https://cdn.test/widget.png", result);
        }

        [Fact]
        public async Task GetChatbotIconUrlAsync_RootContextTrue_PrefersMatchingRowOverIndependentWidgetPreference()
        {
            // rootIsWidget == true means this agent is a subagent invoked via a hand-off whose root is
            // widget-configured — must resolve its OWN widget row even though, in isolation, its most
            // recently updated row is non-widget (proving this isn't just "always prefer widget").
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = true, ChatbotIconPath = "https://cdn.test/sub-widget.png", CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new AgentChatSetting { Id = 2, AgentId = AgentId, IsWidget = false, ChatbotIconPath = "https://cdn.test/sub-non-widget.png", CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) }
            });

            var result = await AgentChatIconHelper.GetChatbotIconUrlAsync(db.Object, AgentId, rootIsWidget: true, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("https://cdn.test/sub-widget.png", result);
        }

        [Fact]
        public async Task GetChatbotIconUrlAsync_RootContextFalseButNoMatchingRow_FallsBackToMostRecentlyUpdated()
        {
            // This agent has no row matching the root's context at all (e.g. only ever configured as
            // widget) — must fail open to its own most-recently-updated row rather than returning null.
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = true, ChatbotIconPath = "https://cdn.test/only-widget.png" }
            });

            var result = await AgentChatIconHelper.GetChatbotIconUrlAsync(db.Object, AgentId, rootIsWidget: false, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("https://cdn.test/only-widget.png", result);
        }

        [Fact]
        public async Task GetChatbotIconUrlAsync_NoRowsAtAll_ReturnsNull()
        {
            var db = BuildDb([]);

            var result = await AgentChatIconHelper.GetChatbotIconUrlAsync(db.Object, AgentId, rootIsWidget: true, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetIsWidgetContextAsync_MultipleRows_ReturnsMostRecentlyUpdatedRowsValue()
        {
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = true, CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new AgentChatSetting { Id = 2, AgentId = AgentId, IsWidget = false, CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) }
            });

            var result = await AgentChatIconHelper.GetIsWidgetContextAsync(db.Object, AgentId, TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task GetIsWidgetContextAsync_UsesLastUpdatedOnOverCreatedOnWhenPresent()
        {
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = false, CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), LastUpdatedOn = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero) },
                new AgentChatSetting { Id = 2, AgentId = AgentId, IsWidget = true, CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) }
            });

            var result = await AgentChatIconHelper.GetIsWidgetContextAsync(db.Object, AgentId, TestContext.Current.CancellationToken);

            // Row 1's LastUpdatedOn (Jan 5) beats row 2's CreatedOn (Jan 2, no LastUpdatedOn) — row 1 wins.
            Assert.False(result);
        }

        [Fact]
        public async Task GetIsWidgetContextAsync_NoRowsForAgent_ReturnsNull()
        {
            var db = BuildDb([]);

            var result = await AgentChatIconHelper.GetIsWidgetContextAsync(db.Object, AgentId, TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetIsWidgetContextAsync_IgnoresSoftDeletedRow()
        {
            var db = BuildDb(new[]
            {
                new AgentChatSetting { Id = 1, AgentId = AgentId, IsWidget = true, IsDeleted = true }
            });

            var result = await AgentChatIconHelper.GetIsWidgetContextAsync(db.Object, AgentId, TestContext.Current.CancellationToken);

            Assert.Null(result);
        }
    }
}
