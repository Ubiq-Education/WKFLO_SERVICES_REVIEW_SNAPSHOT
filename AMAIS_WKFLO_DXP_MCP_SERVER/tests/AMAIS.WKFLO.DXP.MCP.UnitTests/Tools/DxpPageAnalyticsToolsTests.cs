using AMAIS.DXP.Common.Client;
using AMAIS.DXP.REPORT.Client.Lib.Api;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Queries;
using AMAIS.DXP.REPORT.Client.Lib.Models.Documents.Results;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.DXP.MCP.Application.Callers;
using AMAIS.WKFLO.DXP.MCP.Application.Models;
using AMAIS.WKFLO.DXP.MCP.Application.Pages;
using AMAIS.WKFLO.DXP.MCP.Application.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;
using System.Security.Claims;
using System.Text.Json;
using static AMAIS.WKFLO.DXP.MCP.UnitTests.Pages.DxpDocumentsTests;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests.Tools
{
    public class DxpPageAnalyticsToolsTests
    {
        private static readonly DateTime UtcNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly Mock<IReportApi> _reportApi = new();
        private readonly Mock<IDxpPageStatsSource> _pageStatsSource = new();

        private DxpPageAnalyticsTools CreateTools(ClaimsPrincipal principal) =>
            CreateTools(principal, _pageStatsSource.Object);

        /// <summary>Tools wired to the real <see cref="DocumentByPageStatsSource"/> over the mocked Report API client.</summary>
        private DxpPageAnalyticsTools CreateToolsWithReportApiSource(ClaimsPrincipal principal) =>
            CreateTools(principal, new DocumentByPageStatsSource(_reportApi.Object));

        private DxpPageAnalyticsTools CreateTools(ClaimsPrincipal principal, IDxpPageStatsSource pageStatsSource)
        {
            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            httpContextAccessor.Setup(m => m.HttpContext).Returns(new DefaultHttpContext { User = principal });

            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(m => m.DateTimeUtcNow()).Returns(UtcNow);

            return new DxpPageAnalyticsTools(
                _reportApi.Object,
                pageStatsSource,
                httpContextAccessor.Object,
                dateTimeProvider.Object,
                NullLogger<DxpPageAnalyticsTools>.Instance);
        }

        private static bool IsError(TextContentBlock block) => block.Meta != null && block.Meta.ContainsKey("isError");

        [Fact]
        public async Task GetTopPages_SendsTenantFromSiteIdClaim()
        {
            _reportApi
                .Setup(m => m.DocumentsListAsync(It.IsAny<ListDocumentQuery>(), It.IsAny<string>()))
                .ReturnsAsync(new AggregationDocumentResponseResultListSuccessResult { Data = [] });

            var result = await CreateTools(TestPrincipals.Authenticated("site-a"))
                .GetTopPages(cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            _reportApi.Verify(m => m.DocumentsListAsync(It.IsAny<ListDocumentQuery>(), "site-a"), Times.Once);
            _reportApi.Verify(m => m.DocumentsListAsync(It.IsAny<ListDocumentQuery>(), It.Is<string>(t => t != "site-a")), Times.Never);
        }

        [Fact]
        public async Task GetTopPages_WithoutSiteIdClaim_ReturnsErrorWithoutCallingDxp()
        {
            var result = await CreateTools(TestPrincipals.Authenticated(siteId: null))
                .GetTopPages(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            Assert.Contains(DxpCallerContext.MissingTenantError, result.Text);
            _reportApi.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetTopPages_ReturnsCompactPagesSortedAndCapped()
        {
            var documents = Enumerable.Range(1, 80)
                .Select(i => Doc($"https://www.example.org/page-{i}", i))
                .Append(Doc("https://www.example.org/brochure.pdf", 1000, type: "Document"))
                .ToList();
            _reportApi
                .Setup(m => m.DocumentsListAsync(It.IsAny<ListDocumentQuery>(), It.IsAny<string>()))
                .ReturnsAsync(new AggregationDocumentResponseResultListSuccessResult { Data = documents });

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetTopPages(lastDays: 7, top: 500, cancellationToken: TestContext.Current.CancellationToken);

            var payload = JsonSerializer.Deserialize<TopPagesResult>(result.Text, Json)!;
            Assert.Equal(DxpPageAnalyticsTools.MaxTop, payload.Pages.Count);
            Assert.Equal("/page-80", payload.Pages[0].Path);
            Assert.DoesNotContain(payload.Pages, p => p.Url.EndsWith(".pdf"));
            Assert.Equal(new DxpPeriod("2026-10-01", "2026-10-08", 8), payload.Period);
        }

        [Fact]
        public async Task GetTopPages_WhenReportApiFails_ReturnsGenericError()
        {
            _reportApi
                .Setup(m => m.DocumentsListAsync(It.IsAny<ListDocumentQuery>(), It.IsAny<string>()))
                .ThrowsAsync(new ApiException(500, "secret internal detail"));

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetTopPages(cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            Assert.DoesNotContain("secret internal detail", result.Text);
        }

        [Fact]
        public async Task GetPagePerformance_UsesTenantFromClaimAndCanonicalUrlFromAud()
        {
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, null));

            await CreateTools(TestPrincipals.Authenticated("site-a", "https://www.example.org"))
                .GetPagePerformance(pagePath: "/Admissions/", cancellationToken: TestContext.Current.CancellationToken);

            _pageStatsSource.Verify(m => m.GetPagePerformanceAsync(
                "site-a",
                It.Is<DxpPageReference>(p => p.CanonicalUrl == new Uri("https://www.example.org/admissions") && p.PageId == null),
                new DxpDateWindow(new DateTime(2026, 9, 8), new DateTime(2026, 10, 8)),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformance_HomepageUsesSiteRoot()
        {
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, null));

            await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/", cancellationToken: TestContext.Current.CancellationToken);

            _pageStatsSource.Verify(m => m.GetPagePerformanceAsync(
                It.IsAny<string>(), It.Is<DxpPageReference>(p => p.CanonicalUrl == new Uri("https://www.example.org/")), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetPagePerformance_WithoutSiteIdClaim_ReturnsError()
        {
            var result = await CreateTools(TestPrincipals.Authenticated(siteId: null))
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            Assert.Contains(DxpCallerContext.MissingTenantError, result.Text);
            _pageStatsSource.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformance_WithoutAudOrigin_ReturnsError()
        {
            var result = await CreateTools(TestPrincipals.Authenticated(audiences: "not-a-url"))
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            _pageStatsSource.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformance_WithEmptyPath_ReturnsError()
        {
            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "  ", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            _pageStatsSource.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformance_WhenPageNotFound_ReturnsFoundFalseWithNote()
        {
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, null));

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.False(payload.Found);
            Assert.Equal(DxpPageAnalyticsTools.NotFoundNote, payload.Note);
            Assert.Equal(new DxpPeriod("2026-08-08", "2026-09-07", 31), payload.PreviousPeriod);
        }

        [Fact]
        public async Task GetPagePerformance_ReturnsCurrentAndPreviousMetrics()
        {
            var current = PageMetrics.From(new AggregationDocumentResponseResult
            {
                Object = "https://www.example.org/admissions",
                Type = "Page",
                TotalCount = 200,
                UniqueCount = 150,
                EngagementTimeInSecs = 1000,
                BounceRate = 41.234m,
                RecirculationRate = 30m,
                DeadPageRate = 28.766m
            });
            var previous = current with { Views = 100 };
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(current, previous));

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.True(payload.Found);
            Assert.Equal(200, payload.Current!.Views);
            Assert.Equal(41.2m, payload.Current.BounceRate);
            Assert.Equal(5.0, payload.Current.AvgEngagementSecs);
            Assert.Equal(100, payload.Previous!.Views);
        }

        [Fact]
        public async Task GetPagePerformance_WhenOnlyPreviousPeriodHasVisits_ReturnsFoundWithNoCurrentVisitsNote()
        {
            var previous = PageMetrics.From(new AggregationDocumentResponseResult
            {
                Object = "https://www.example.org/admissions",
                Type = "Page",
                TotalCount = 80,
                UniqueCount = 60
            });
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, previous));

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.True(payload.Found);
            Assert.Null(payload.Current);
            Assert.Equal(80, payload.Previous!.Views);
            Assert.Equal(DxpPageAnalyticsTools.NoCurrentVisitsNote, payload.Note);
        }

        [Fact]
        public async Task GetPagePerformance_WhenSourceNotesUrlFallbackAndNoCurrentVisits_ReturnsBothNotes()
        {
            var previous = PageMetrics.From(new AggregationDocumentResponseResult
            {
                Object = "https://www.example.org/admissions",
                Type = "Page",
                TotalCount = 80,
                UniqueCount = 60
            });
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, previous, "matched by URL"));

            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.True(payload.Found);
            Assert.Equal("matched by URL " + DxpPageAnalyticsTools.NoCurrentVisitsNote, payload.Note);
        }

        [Theory]
        [InlineData(0, DxpPageAnalyticsTools.DefaultLastDays)]
        [InlineData(-5, DxpPageAnalyticsTools.DefaultLastDays)]
        [InlineData(7, 7)]
        [InlineData(10_000, DxpPageAnalyticsTools.MaxLastDays)]
        public void ClampLastDays_KeepsValueInRange(int input, int expected)
        {
            Assert.Equal(expected, DxpPageAnalyticsTools.ClampLastDays(input));
        }

        [Theory]
        [InlineData(0, DxpPageAnalyticsTools.DefaultTop)]
        [InlineData(5, 5)]
        [InlineData(51, DxpPageAnalyticsTools.MaxTop)]
        public void ClampTop_KeepsValueInRange(int input, int expected)
        {
            Assert.Equal(expected, DxpPageAnalyticsTools.ClampTop(input));
        }

        [Fact]
        public async Task GetPagePerformance_WithPageIdAndPath_PassesBoth()
        {
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, null));

            var result = await CreateTools(TestPrincipals.Authenticated("site-a"))
                .GetPagePerformance(pageId: " 1234 ", pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            _pageStatsSource.Verify(m => m.GetPagePerformanceAsync(
                "site-a",
                It.Is<DxpPageReference>(p => p.PageId == "1234" && p.CanonicalUrl == new Uri("https://www.example.org/admissions")),
                It.IsAny<DxpDateWindow>(),
                It.IsAny<CancellationToken>()), Times.Once);
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.Equal("1234", payload.PageId);
        }

        [Fact]
        public async Task GetPagePerformance_WithPageIdOnly_DoesNotNeedAudOrigin()
        {
            _pageStatsSource
                .Setup(m => m.GetPagePerformanceAsync(It.IsAny<string>(), It.IsAny<DxpPageReference>(), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DxpPagePerformance(null, null, "note from source"));

            var result = await CreateTools(TestPrincipals.Authenticated(audiences: "not-a-url"))
                .GetPagePerformance(pageId: "1234", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            _pageStatsSource.Verify(m => m.GetPagePerformanceAsync(
                It.IsAny<string>(), It.Is<DxpPageReference>(p => p.PageId == "1234" && p.CanonicalUrl == null), It.IsAny<DxpDateWindow>(), It.IsAny<CancellationToken>()), Times.Once);
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.Equal("note from source " + DxpPageAnalyticsTools.NotFoundNote, payload.Note);
        }

        [Fact]
        public async Task GetPagePerformance_WithNeitherPageIdNorPath_ReturnsError()
        {
            var result = await CreateTools(TestPrincipals.Authenticated())
                .GetPagePerformance(pageId: " ", pagePath: null, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            _pageStatsSource.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformance_WithPageIdButNoSiteIdClaim_ReturnsError()
        {
            var result = await CreateTools(TestPrincipals.Authenticated(siteId: null))
                .GetPagePerformance(pageId: "1234", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            Assert.Contains(DxpCallerContext.MissingTenantError, result.Text);
            _pageStatsSource.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetPagePerformance_SendsTenantFromSiteIdClaimToDocumentByPage()
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult
                {
                    Data = new DocumentByPageGetResult { PageId = "1234", Url = "https://www.example.org/admissions", TotalCount = 20 }
                });

            var result = await CreateToolsWithReportApiSource(TestPrincipals.Authenticated("site-a"))
                .GetPagePerformance(pageId: "1234", pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            _reportApi.Verify(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.PageId == "1234"), "site-a"), Times.Once);
            _reportApi.Verify(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.Is<string>(t => t != "site-a")), Times.Never);
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.True(payload.Found);
            Assert.Equal(20, payload.Current!.Views);
        }

        [Fact]
        public async Task GetPagePerformance_WhenDocumentByPageHasNoVisits_ReturnsFoundFalse()
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult
                {
                    Data = new DocumentByPageGetResult { Url = "https://www.example.org/admissions", PreviousPeriod = new DocumentPageStatsResult() }
                });

            var result = await CreateToolsWithReportApiSource(TestPrincipals.Authenticated())
                .GetPagePerformance(pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.False(payload.Found);
            Assert.Null(payload.Current);
            Assert.Null(payload.Previous);
            Assert.Equal(DxpPageAnalyticsTools.NotFoundNote, payload.Note);
        }

        [Fact]
        public async Task GetPagePerformance_WhenPageIdAndUrlFallbackHaveNoVisits_ReturnsFoundFalseUsingClaimTenantForBoth()
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .ReturnsAsync(new DocumentByPageGetResultSuccessResult
                {
                    Data = new DocumentByPageGetResult { PreviousPeriod = new DocumentPageStatsResult() }
                });

            var result = await CreateToolsWithReportApiSource(TestPrincipals.Authenticated("site-a"))
                .GetPagePerformance(pageId: "1234", pagePath: "/admissions", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(IsError(result));
            _reportApi.Verify(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.PageId == "1234" && q.Url == null), "site-a"), Times.Once);
            _reportApi.Verify(m => m.DocumentByPageAsync(It.Is<GetDocumentByPageQuery>(q => q.Url == "https://www.example.org/admissions" && q.PageId == null), "site-a"), Times.Once);
            _reportApi.Verify(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.Is<string>(t => t != "site-a")), Times.Never);
            var payload = JsonSerializer.Deserialize<PagePerformanceResult>(result.Text, Json)!;
            Assert.False(payload.Found);
        }

        [Fact]
        public async Task GetPagePerformance_WhenDocumentByPageRejectsRequest_ReturnsGenericError()
        {
            _reportApi
                .Setup(m => m.DocumentByPageAsync(It.IsAny<GetDocumentByPageQuery>(), It.IsAny<string>()))
                .ThrowsAsync(new ApiException(400, "Error calling DocumentByPageAsync: secret validation detail"));

            var result = await CreateToolsWithReportApiSource(TestPrincipals.Authenticated())
                .GetPagePerformance(pageId: "1234", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(IsError(result));
            Assert.Contains(DxpPageAnalyticsTools.ReportApiError, result.Text);
            Assert.DoesNotContain("secret validation detail", result.Text);
        }
    }
}
