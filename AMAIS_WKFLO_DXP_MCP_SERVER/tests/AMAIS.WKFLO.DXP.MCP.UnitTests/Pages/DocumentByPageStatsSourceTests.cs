using AMAIS.DXP.Common.Client;
using AMAIS.DXP.REPORT.Client.Lib.Api;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Queries;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;
using Moq;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests.Pages
{
    public class DocumentByPageStatsSourceTests
    {
        private static readonly DxpDateWindow Period = new(new DateTime(2026, 9, 8), new DateTime(2026, 10, 8));
        private static readonly Uri AdmissionsUrl = new("https://www.example.org/admissions");

        private readonly Mock<IReportApi> _reportApi = new();
        private GetDocumentByPageQuery? _sentQuery;

        private DocumentByPageStatsSource CreateSource(DocumentByPageGetResult? data)
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .Callback<GetDocumentByPageQuery, string>((q, _) => _sentQuery = q)
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult { Data = data! });

            return new DocumentByPageStatsSource(_reportApi.Object);
        }

        internal static DocumentByPageGetResult Result(string? url, long views, DocumentPageStatsResult? previous = null, string? pageId = null) => new()
        {
            Url = url!,
            PageId = pageId!,
            TotalCount = views,
            UniqueCount = (int)views / 2,
            EngagementTimeInSecs = views * 10,
            BounceRate = 41.234m,
            PreviousPeriod = previous!
        };

        internal static DocumentPageStatsResult Stats(string? url, long views) => new() { Url = url!, TotalCount = views };

        [Fact]
        public async Task GetPagePerformanceAsync_WithPageId_SendsOnlyPageIdForPeriod()
        {
            var source = CreateSource(Result("https://www.example.org/home", 50, pageId: "42"));

            await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.NotNull(_sentQuery);
            Assert.Equal("42", _sentQuery!.PageId);
            Assert.Null(_sentQuery.Url);
            Assert.Equal(Period.From, _sentQuery.DateFrom);
            Assert.Equal(Period.To, _sentQuery.DateTo);
            _reportApi.Verify(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WithoutPageId_SendsAbsoluteCanonicalUrl()
        {
            var source = CreateSource(Result(AdmissionsUrl.AbsoluteUri, 50));

            await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.NotNull(_sentQuery);
            Assert.Equal("https://www.example.org/admissions", _sentQuery!.Url);
            Assert.Null(_sentQuery.PageId);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_PassesTenantUnchanged()
        {
            var source = CreateSource(Result(AdmissionsUrl.AbsoluteUri, 50));

            await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            _reportApi.Verify(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), "tenant-a"), Times.Once);
            _reportApi.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformanceAsync_MapsCurrentAndPreviousPeriodFromOneCall()
        {
            var source = CreateSource(Result("https://www.example.org/home", 200, previous: Stats("https://www.example.org/home", 80), pageId: "42"));

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", null), Period, TestContext.Current.CancellationToken);

            Assert.Equal(200, result.Current!.Views);
            Assert.Equal(100, result.Current.UniqueVisitors);
            Assert.Equal(10.0, result.Current.AvgEngagementSecs);
            Assert.Equal(41.2m, result.Current.BounceRate);
            Assert.Equal("/home", result.Current.Path);
            Assert.Equal("https://www.example.org/home", result.Current.Url);
            Assert.Null(result.Current.LastVisit);
            Assert.Equal(80, result.Previous!.Views);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenNoVisits_ReturnsNulls()
        {
            // The endpoint answers a page without data with zero counts (and, by page ID, no URL) instead of a 404.
            var source = CreateSource(Result(null, 0, previous: Stats(null, 0), pageId: "42"));

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", null), Period, TestContext.Current.CancellationToken);

            Assert.Null(result.Current);
            Assert.Null(result.Previous);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenNoData_ReturnsNulls()
        {
            var source = CreateSource(null);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Null(result.Current);
            Assert.Null(result.Previous);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenOnlyPreviousPeriodHasVisits_ReturnsPreviousOnly()
        {
            var source = CreateSource(Result(AdmissionsUrl.AbsoluteUri, 0, previous: Stats(AdmissionsUrl.AbsoluteUri, 30)));

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Null(result.Current);
            Assert.Equal(30, result.Previous!.Views);
        }

        private DocumentByPageStatsSource CreateSource(DocumentByPageGetResult byPageId, DocumentByPageGetResult byUrl, List<(GetDocumentByPageQuery Query, string Tenant)> calls)
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.PageId != null), It.IsAny<string>()))
                .Callback<GetDocumentByPageQuery, string>((q, t) => calls.Add((q, t)))
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult { Data = byPageId });
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.Url != null), It.IsAny<string>()))
                .Callback<GetDocumentByPageQuery, string>((q, t) => calls.Add((q, t)))
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult { Data = byUrl });

            return new DocumentByPageStatsSource(_reportApi.Object);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdHasNoVisits_FallsBackOnceToOwnUrl()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(
                byPageId: Result(null, 0, previous: Stats(null, 0), pageId: "42"),
                byUrl: Result(AdmissionsUrl.AbsoluteUri, 70, previous: Stats(AdmissionsUrl.AbsoluteUri, 30)),
                calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Equal(2, calls.Count);
            Assert.Equal("42", calls[0].Query.PageId);
            Assert.Null(calls[0].Query.Url);
            Assert.Equal("https://www.example.org/admissions", calls[1].Query.Url);
            Assert.Null(calls[1].Query.PageId);
            Assert.Equal(Period.From, calls[1].Query.DateFrom);
            Assert.Equal(Period.To, calls[1].Query.DateTo);
            Assert.Equal(70, result.Current!.Views);
            Assert.Equal("/admissions", result.Current.Path);
            Assert.Equal(30, result.Previous!.Views);
            Assert.Equal(DocumentByPageStatsSource.UrlFallbackNote, result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_FallbackUsesSameTenantOnBothCalls()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(Result(null, 0, pageId: "42"), Result(AdmissionsUrl.AbsoluteUri, 5), calls);

            await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Equal(["tenant-a", "tenant-a"], calls.Select(c => c.Tenant));
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdHasVisits_DoesNotFallBack()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(Result("https://www.example.org/admissions", 12, pageId: "42"), Result(AdmissionsUrl.AbsoluteUri, 999), calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Single(calls);
            Assert.Equal(12, result.Current!.Views);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdHasOnlyPreviousVisits_DoesNotFallBack()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(
                Result(null, 0, previous: Stats("https://www.example.org/admissions", 9), pageId: "42"),
                Result(AdmissionsUrl.AbsoluteUri, 999),
                calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Single(calls);
            Assert.Null(result.Current);
            Assert.Equal(9, result.Previous!.Views);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenUrlFallbackHasOnlyPreviousVisits_AddsFallbackNote()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(
                Result(null, 0, previous: Stats(null, 0), pageId: "42"),
                Result(AdmissionsUrl.AbsoluteUri, 0, previous: Stats(AdmissionsUrl.AbsoluteUri, 15)),
                calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Equal(2, calls.Count);
            Assert.Null(result.Current);
            Assert.Equal(15, result.Previous!.Views);
            Assert.Equal(DocumentByPageStatsSource.UrlFallbackNote, result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WithoutPageId_DoesNotAddFallbackNote()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(byPageId: Result(null, 999, pageId: "42"), byUrl: Result(AdmissionsUrl.AbsoluteUri, 25), calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Single(calls);
            Assert.Equal(25, result.Current!.Views);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdHasNoVisitsAndNoPath_DoesNotFallBack()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(Result(null, 0, pageId: "42"), Result("https://www.example.org/", 500), calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", null), Period, TestContext.Current.CancellationToken);

            Assert.Single(calls);
            Assert.Null(result.Current);
            _reportApi.Verify(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.Url != null), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("https://www.example.org/")]
        [InlineData("https://www.example.org")]
        public async Task GetPagePerformanceAsync_WhenPageIdHasNoVisits_NeverFallsBackToSiteRoot(string siteRoot)
        {
            // A homepage alias like "/home" must not be reported with the site root's figures: that would mislabel
            // another page's data whenever this page simply has no visits.
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(Result(null, 0, pageId: "42"), Result("https://www.example.org/", 500), calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", new Uri(siteRoot)), Period, TestContext.Current.CancellationToken);

            Assert.Single(calls);
            Assert.Null(result.Current);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdAndUrlBothHaveNoVisits_ReturnsNulls()
        {
            var calls = new List<(GetDocumentByPageQuery Query, string Tenant)>();
            var source = CreateSource(
                Result(null, 0, previous: Stats(null, 0), pageId: "42"),
                Result(AdmissionsUrl.AbsoluteUri, 0, previous: Stats(AdmissionsUrl.AbsoluteUri, 0)),
                calls);

            var result = await source.GetPagePerformanceAsync("tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken);

            Assert.Equal(2, calls.Count);
            Assert.Null(result.Current);
            Assert.Null(result.Previous);
            Assert.Null(result.Note);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenPageIdCallFails_DoesNotFallBack()
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.PageId != null), It.IsAny<string>()))
                .ThrowsAsync(new ApiException(500, "boom"));
            var source = new DocumentByPageStatsSource(_reportApi.Object);

            await Assert.ThrowsAsync<ApiException>(() => source.GetPagePerformanceAsync(
                "tenant-a", new DxpPageReference("42", AdmissionsUrl), Period, TestContext.Current.CancellationToken));

            _reportApi.Verify(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformanceAsync_WhenReportApiFails_PropagatesApiException()
        {
            // The tool turns this into a generic error for the model; the source must not swallow it as "not found".
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .ThrowsAsync(new ApiException(400, "validation detail"));
            var source = new DocumentByPageStatsSource(_reportApi.Object);

            await Assert.ThrowsAsync<ApiException>(() => source.GetPagePerformanceAsync(
                "tenant-a", new DxpPageReference(null, AdmissionsUrl), Period, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void DateWindow_ForLastDays_MatchesReportApiWindowAndPreviousHasSameLength()
        {
            var window = DxpDateWindow.ForLastDays(new DateTime(2026, 10, 8, 15, 30, 0, DateTimeKind.Utc), 30);
            var previous = window.Previous();

            Assert.Equal(new DateTime(2026, 9, 8), window.From);
            Assert.Equal(new DateTime(2026, 10, 8), window.To);
            Assert.Equal(31, window.LengthInDays);
            Assert.Equal(new DateTime(2026, 8, 8), previous.From);
            Assert.Equal(new DateTime(2026, 9, 7), previous.To);
            Assert.Equal(window.LengthInDays, previous.LengthInDays);
        }

        [Fact]
        public void PageReference_RequiresPageIdOrUrl()
        {
            Assert.Throws<ArgumentException>(() => new DxpPageReference("  ", null));
            Assert.Equal("42", new DxpPageReference(" 42 ", null).PageId);
        }
    }
}
