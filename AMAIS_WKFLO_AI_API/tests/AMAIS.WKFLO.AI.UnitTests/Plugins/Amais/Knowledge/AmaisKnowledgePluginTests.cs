using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.Knowledge
{
    public class AmaisKnowledgePluginTests
    {
        // ── GetSchoolKnowledgeAsync → EisKnowledgeQueryOptions mapping ───────

        private static AmaisKnowledgePlugin BuildPlugin(Mock<IEisKnowledgeProvider> eisKnowledgeProviderMock)
        {
            eisKnowledgeProviderMock
                .Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EisKnowledgeQueryOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<EisDocument>)new List<EisDocument>());

            return new AmaisKnowledgePlugin(
                eisKnowledgeProviderMock.Object,
                Mock.Of<IChatNotificationService>(),
                new TenantContext { TenantName = "school-a" },
                new ChatContext { SessionId = "session-1" });
        }

        [Fact]
        public async Task GetSchoolKnowledgeAsync_WithEventDateRangeStart_MapsToEventDateRangeStartUtc()
        {
            var eisKnowledgeProviderMock = new Mock<IEisKnowledgeProvider>();
            var plugin = BuildPlugin(eisKnowledgeProviderMock);
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

            await plugin.GetSchoolKnowledgeAsync("What upcoming events do you have?", asOfUtc, eventDateRangeStartUtc: asOfUtc, eventDateRangeEndUtc: null);

            eisKnowledgeProviderMock.Verify(x => x.RetrieveAsync(
                "school-a",
                "What upcoming events do you have?",
                It.Is<EisKnowledgeQueryOptions>(o =>
                    o.EventDateRangeStartUtc == asOfUtc &&
                    o.EventDateRangeEndUtc == null &&
                    o.AsOfUtc == asOfUtc &&
                    o.BranchId == null),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetSchoolKnowledgeAsync_WithEventDateRangeEnd_MapsToEventDateRangeEndUtc()
        {
            var eisKnowledgeProviderMock = new Mock<IEisKnowledgeProvider>();
            var plugin = BuildPlugin(eisKnowledgeProviderMock);
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

            await plugin.GetSchoolKnowledgeAsync("What happened last month?", asOfUtc, eventDateRangeStartUtc: null, eventDateRangeEndUtc: end);

            eisKnowledgeProviderMock.Verify(x => x.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<EisKnowledgeQueryOptions>(o => o.EventDateRangeStartUtc == null && o.EventDateRangeEndUtc == end),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetSchoolKnowledgeAsync_WithNeitherBound_MapsToNullRange()
        {
            var eisKnowledgeProviderMock = new Mock<IEisKnowledgeProvider>();
            var plugin = BuildPlugin(eisKnowledgeProviderMock);

            await plugin.GetSchoolKnowledgeAsync("What is the school's mascot?");

            eisKnowledgeProviderMock.Verify(x => x.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<EisKnowledgeQueryOptions>(o => o.EventDateRangeStartUtc == null && o.EventDateRangeEndUtc == null),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetSchoolKnowledgeAsync_WithBothBoundsSet_MapsBothThrough()
        {
            var eisKnowledgeProviderMock = new Mock<IEisKnowledgeProvider>();
            var plugin = BuildPlugin(eisKnowledgeProviderMock);
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

            await plugin.GetSchoolKnowledgeAsync("message", DateTimeOffset.UtcNow, eventDateRangeStartUtc: start, eventDateRangeEndUtc: end);

            eisKnowledgeProviderMock.Verify(x => x.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<EisKnowledgeQueryOptions>(o => o.EventDateRangeStartUtc == start && o.EventDateRangeEndUtc == end),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        // ── ResolveRelativeLinks ─────────────────────────────────────────────

        [Fact]
        public void ResolveRelativeLinks_WhenLinksEmpty_ReturnsEmpty()
        {
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(string.Empty, "https://example.com");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ResolveRelativeLinks_WhenBaseUrlNull_ReturnsLinksUnchanged()
        {
            var links = @"Page link:  [{""title"": ""Test"", ""url"": ""/test""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, null);
            Assert.Equal(links, result);
        }

        [Fact]
        public void ResolveRelativeLinks_WhenBaseUrlEmpty_ReturnsLinksUnchanged()
        {
            var links = @"Page link:  [{""title"": ""Test"", ""url"": ""/test""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, string.Empty);
            Assert.Equal(links, result);
        }

        [Fact]
        public void ResolveRelativeLinks_WithRelativeUrl_PrependsBaseUrl()
        {
            var links = @"Page link:  [{""title"": ""Dan Is Testing"", ""url"": ""/dan-is-testing""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, "https://example.com");
            Assert.Contains(@"""url"": ""https://example.com/dan-is-testing""", result);
        }

        [Fact]
        public void ResolveRelativeLinks_BaseUrlWithTrailingSlash_DoesNotDoubleSlash()
        {
            var links = @"Page link:  [{""title"": ""Test"", ""url"": ""/page""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, "https://example.com/");
            Assert.Contains(@"""url"": ""https://example.com/page""", result);
            Assert.DoesNotContain("//page", result);
        }

        [Fact]
        public void ResolveRelativeLinks_WithMultipleRelativeUrls_PrependsAllOfThem()
        {
            var links = @"Page link:  [{""title"": ""A"", ""url"": ""/a""}, {""title"": ""B"", ""url"": ""/b""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, "https://example.com");
            Assert.Contains(@"""url"": ""https://example.com/a""", result);
            Assert.Contains(@"""url"": ""https://example.com/b""", result);
        }

        [Fact]
        public void ResolveRelativeLinks_WithAbsoluteUrl_LeavesItUnchanged()
        {
            var links = @"Page link:  [{""title"": ""Ext"", ""url"": ""https://other.com/page""}]";
            var result = AmaisKnowledgePlugin.ResolveRelativeLinks(links, "https://example.com");
            Assert.Equal(links, result);
        }

        // ── BuildKnowledgeOutputText ─────────────────────────────────────────

        [Fact]
        public void BuildKnowledgeOutputText_WithNoDocs_ReturnsNoPassagesMessage()
        {
            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText([], "https://example.com");
            Assert.Contains("No relevant knowledge base passages were found", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithDocs_ContainsNameAndPassage()
        {
            var docs = new List<EisDocument>
            {
                new() { Name = "Doc1", Content = "Some content", Links = string.Empty }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, null);

            Assert.Contains("NAME: Doc1", result);
            Assert.Contains("PASSAGE: Some content", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithRelativeLinks_ResolvesUrls()
        {
            var docs = new List<EisDocument>
            {
                new()
                {
                    Name = "Doc1",
                    Content = "Content",
                    Links = @"Page link:  [{""title"": ""Dan Is Testing"", ""url"": ""/dan-is-testing""}]"
                }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, "https://example.com");

            Assert.Contains("https://example.com/dan-is-testing", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithNullBaseUrl_KeepsRelativeLinksAsIs()
        {
            var docs = new List<EisDocument>
            {
                new()
                {
                    Name = "Doc1",
                    Content = "Content",
                    Links = @"Page link:  [{""title"": ""Test"", ""url"": ""/test""}]"
                }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, null);

            Assert.Contains(@"""url"": ""/test""", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithEventEndDate_AnnotatesEventDate()
        {
            var docs = new List<EisDocument>
            {
                new() { Name = "Open House", Content = "Details", Links = string.Empty, EventEndDate = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero) }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, null);

            Assert.Contains("EVENT DATE: 2026-08-15", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithContentUpdatedAt_AnnotatesLastUpdated()
        {
            var docs = new List<EisDocument>
            {
                new() { Name = "Admissions", Content = "Details", Links = string.Empty, ContentUpdatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero) }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, null);

            Assert.Contains("LAST UPDATED: 2026-06-01", result);
        }

        [Fact]
        public void BuildKnowledgeOutputText_WithoutEventOrUpdateDates_OmitsAnnotations()
        {
            var docs = new List<EisDocument>
            {
                new() { Name = "Doc1", Content = "Content", Links = string.Empty }
            };

            var result = AmaisKnowledgePlugin.BuildKnowledgeOutputText(docs, null);

            Assert.DoesNotContain("EVENT DATE:", result);
            Assert.DoesNotContain("LAST UPDATED:", result);
        }
    }
}
