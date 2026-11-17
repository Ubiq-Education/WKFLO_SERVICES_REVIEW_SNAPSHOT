using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Audit.Models;
using AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Audit.Queries.ListAuditLog
{
    public class ListAuditLogQueryHandlerTests : IClassFixture<ListAuditLogQueryHandlerTesting>
    {
        private readonly ListAuditLogQueryHandlerTesting _testing;

        public ListAuditLogQueryHandlerTests(ListAuditLogQueryHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData(1, 3, 3, 2, ResultType.Ok)]
        [InlineData(2, 3, 3, 2, ResultType.Ok)]
        [InlineData(1, 15, 6, 1, ResultType.Ok)]
        public async Task TestHandler_Pagination(int page, int pageSize, int expectedItemCount, int expectedTotalPages, ResultType resultType)
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Page = page,
                PageSize = pageSize
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<AuditLogListPageResult> success)
            {
                Assert.NotNull(success.Data);
                Assert.Equal(6, success.Data.TotalCount);
                Assert.Equal(expectedItemCount, success.Data.Items.Count);
                Assert.Equal(expectedTotalPages, success.Data.TotalPages);
                Assert.Equal(page, success.Data.Page);
                Assert.Equal(pageSize, success.Data.PageSize);
            }
            else
            {
                Assert.Fail($"Expected SuccessResult but got {result.ResultType}");
            }
        }

        [Fact]
        public async Task TestHandler_ScopesToCurrentTenant_ExcludesOtherTenantRows()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(6, success.Data.TotalCount);
            Assert.DoesNotContain(success.Data.Items, i => i.ActorUserName == "Eve");
        }

        [Fact]
        public async Task TestHandler_FilterByEntityType()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                EntityType = "Tag",
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(2, success.Data.TotalCount);
            Assert.All(success.Data.Items, i => Assert.Equal("Tag", i.EntityType));
        }

        [Fact]
        public async Task TestHandler_FilterByAction()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Action = CommonConsts.AuditActionConsts.Create,
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert — Ids 1, 4, 5, 7 are Creates for this tenant.
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(4, success.Data.TotalCount);
            Assert.All(success.Data.Items, i => Assert.Equal(CommonConsts.AuditActionConsts.Create, i.Action));
        }

        [Fact]
        public async Task TestHandler_FilterByActor_MatchesNameSubstring()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Actor = "Bob",
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(3, success.Data.TotalCount);
            Assert.All(success.Data.Items, i => Assert.Equal("Bob", i.ActorUserName));
        }

        [Theory]
        [InlineData("bob")]
        [InlineData("BOB")]
        [InlineData("bOb")]
        public async Task TestHandler_FilterByActor_IsCaseInsensitive(string actorFilter)
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Actor = actorFilter,
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(3, success.Data.TotalCount);
            Assert.All(success.Data.Items, i => Assert.Equal("Bob", i.ActorUserName));
        }

        [Fact]
        public async Task TestHandler_FilterByActor_MatchesEmailCaseInsensitiveSubstring()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Actor = "CAROL@TEST.COM",
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            var item = Assert.Single(success.Data.Items);
            Assert.Equal("Carol", item.ActorUserName);
        }

        [Fact]
        public async Task TestHandler_FilterByDateFrom()
        {
            // Act — Ids 1, 2 (at -1/-2 minutes) and 7 (23:30 same day) fall on or after 11:57:30; the -3
            // minute entry (Id 3, at 11:57:00 exactly) falls just before the cutoff.
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                DateFrom = new DateTimeOffset(2026, 1, 1, 11, 57, 30, TimeSpan.Zero),
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(3, success.Data.TotalCount);
        }

        [Fact]
        public async Task TestHandler_DateTo_IsInclusiveOfWholeDay()
        {
            // Act — DateTo is midnight of 2026-01-01 (what the UI sends for a bare date picked as
            // "2026-01-01"). Every fixture row for this tenant is timestamped later that same day (11:55am
            // through 11:59pm), so a naive "Timestamp <= DateTo" would wrongly exclude all of them; the fix
            // must include the whole day instead.
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                DateTo = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(6, success.Data.TotalCount);

            // And the following day is correctly excluded.
            var resultNextDay = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                DateFrom = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);
            var successNextDay = Assert.IsType<SuccessResult<AuditLogListPageResult>>(resultNextDay);
            Assert.Equal(0, successNextDay.Data.TotalCount);
        }

        [Fact]
        public async Task TestHandler_OrdersByTimestampDescending()
        {
            // Act
            var result = await _testing.ListAuditLogQueryHandler.Handle(new ListAuditLogQuery
            {
                Page = 1,
                PageSize = 15
            }, TestContext.Current.CancellationToken);

            // Assert — Id 7 (23:30) is the most recent of the tenant's rows; Id 5 (-5 minutes) the oldest.
            var success = Assert.IsType<SuccessResult<AuditLogListPageResult>>(result);
            Assert.Equal(7, success.Data.Items[0].Id);
            Assert.Equal(5, success.Data.Items[^1].Id);
        }
    }
}
