using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais
{
    public class AmaisPluginCatalogTests
    {
        // ── Catalog shape ────────────────────────────────────────────────────

        [Fact]
        public void All_Knowledge_HasNullEndpointsByToolName()
        {
            // Knowledge is Elasticsearch-backed, not tied to any PetraCMS endpoint — never gated.
            var entry = AmaisPluginCatalog.All.Single(e => e.Name == "Knowledge");
            Assert.Null(entry.EndpointsByToolName);
        }

        [Theory]
        [InlineData("Welcome")]
        [InlineData("Post")]
        [InlineData("Page")]
        [InlineData("GalleryFeature")]
        [InlineData("TextFeature")]
        public void All_EndpointBackedEntry_HasNonEmptyEndpointsByToolName(string name)
        {
            var entry = AmaisPluginCatalog.All.Single(e => e.Name == name);
            Assert.NotNull(entry.EndpointsByToolName);
            Assert.NotEmpty(entry.EndpointsByToolName);
        }

        [Theory]
        [InlineData("Knowledge")]
        [InlineData("Welcome")]
        [InlineData("Post")]
        [InlineData("Page")]
        [InlineData("GalleryFeature")]
        [InlineData("TextFeature")]
        public void All_ContainsExpectedEntry(string name)
        {
            Assert.Contains(AmaisPluginCatalog.All, e => e.Name == name);
        }

        // ── IsFailOpen ───────────────────────────────────────────────────────

        [Fact]
        public void IsFailOpen_NullList_ReturnsTrue()
        {
            Assert.True(AmaisPluginCatalog.IsFailOpen(null));
        }

        [Fact]
        public void IsFailOpen_EmptyList_ReturnsTrue()
        {
            // Empty means the caller's AMAIS build predates this mechanism — treated the same as
            // a missing header, per retro-compatibility requirements.
            Assert.True(AmaisPluginCatalog.IsFailOpen([]));
        }

        [Fact]
        public void IsFailOpen_NonEmptyList_ReturnsFalse()
        {
            Assert.False(AmaisPluginCatalog.IsFailOpen(["WLC"]));
        }

        // ── IsOfferable ──────────────────────────────────────────────────────

        [Fact]
        public void IsOfferable_UnknownName_ReturnsFalse()
        {
            Assert.False(AmaisPluginCatalog.IsOfferable("NotARealPlugin", null));
        }

        [Fact]
        public void IsOfferable_KnowledgeIsNeverGated_EvenWithNonEmptyList()
        {
            Assert.True(AmaisPluginCatalog.IsOfferable("Knowledge", ["CRP"]));
        }

        [Fact]
        public void IsOfferable_NoHeaderSent_ReturnsTrue()
        {
            // Fail-open: identical to pre-ticket behavior.
            Assert.True(AmaisPluginCatalog.IsOfferable("Post", null));
        }

        [Fact]
        public void IsOfferable_HeaderPresentWithAtLeastOneMatchingEndpoint_ReturnsTrue()
        {
            Assert.True(AmaisPluginCatalog.IsOfferable("Post", ["CRP"]));
        }

        [Fact]
        public void IsOfferable_HeaderPresentWithNoMatchingEndpoint_ReturnsFalse()
        {
            Assert.False(AmaisPluginCatalog.IsOfferable("Post", ["WLC"]));
        }

        [Fact]
        public void IsOfferable_IsCaseInsensitive()
        {
            Assert.True(AmaisPluginCatalog.IsOfferable("post", ["crp"]));
        }

        // ── GetOfferable ─────────────────────────────────────────────────────

        [Fact]
        public void GetOfferable_NoHeaderSent_ReturnsAllCandidates()
        {
            var candidates = new[] { "Welcome", "Post", "Page", "GalleryFeature", "TextFeature" };

            var result = AmaisPluginCatalog.GetOfferable(null, candidates);

            Assert.Equal(candidates.Length, result.Count);
            foreach (var name in candidates)
            {
                Assert.Contains(result, e => e.Name == name);
            }
        }

        [Fact]
        public void GetOfferable_HeaderPresent_OnlyKeepsCandidatesWithAMatchingEndpoint()
        {
            var candidates = new[] { "Welcome", "Post", "Page", "GalleryFeature", "TextFeature" };

            var result = AmaisPluginCatalog.GetOfferable(["WLC", "CRP"], candidates);

            Assert.Equal(["Welcome", "Post"], result.Select(e => e.Name).ToArray());
        }

        [Fact]
        public void GetOfferable_CandidateNotInCatalog_IsSilentlyDropped()
        {
            var result = AmaisPluginCatalog.GetOfferable(null, ["Welcome", "NotARealPlugin"]);

            Assert.Single(result);
            Assert.Equal("Welcome", result[0].Name);
        }

        [Fact]
        public void GetOfferable_EmptyCandidateList_ReturnsEmpty()
        {
            var result = AmaisPluginCatalog.GetOfferable(null, []);

            Assert.Empty(result);
        }

        [Fact]
        public void GetOfferable_NullCandidateList_ReturnsEmpty()
        {
            var result = AmaisPluginCatalog.GetOfferable(null, null);

            Assert.Empty(result);
        }

        [Fact]
        public void GetOfferable_PreservesCatalogOrder()
        {
            // Pass candidates in reverse order — result must still follow AmaisPluginCatalog.All's order.
            var result = AmaisPluginCatalog.GetOfferable(null, ["TextFeature", "Welcome", "Page"]);

            Assert.Equal(["Welcome", "Page", "TextFeature"], result.Select(e => e.Name).ToArray());
        }

        // ── PageAnalytics (DXP, fail-closed) ─────────────────────────────────

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsOfferable_PageAnalytics_IsFailClosedWhenHeaderMissingOrEmpty(bool headerMissing)
        {
            string[] availableEndpoints = headerMissing ? null : [];

            // Unlike the other skills, an old PetraCMS build (no header) must not get DXP tools: the site may
            // not have DXP configured at all.
            Assert.False(AmaisPluginCatalog.IsOfferable(AmaisPluginCatalog.PageAnalyticsName, availableEndpoints));
        }

        [Fact]
        public void IsOfferable_PageAnalytics_RequiresDxpPageAnalyticsCode()
        {
            Assert.False(AmaisPluginCatalog.IsOfferable(AmaisPluginCatalog.PageAnalyticsName, ["GPA", "CRP"]));
            Assert.True(AmaisPluginCatalog.IsOfferable(AmaisPluginCatalog.PageAnalyticsName, ["GPA", "DxpPageAnalytics"]));
            Assert.True(AmaisPluginCatalog.IsOfferable(AmaisPluginCatalog.PageAnalyticsName, ["dxppageanalytics"]));
        }

        [Fact]
        public void All_PageAnalytics_MapsBothToolsToCapabilityCode()
        {
            var entry = AmaisPluginCatalog.All.Single(e => e.Name == AmaisPluginCatalog.PageAnalyticsName);

            Assert.True(entry.RequiresCapability);
            Assert.Equal(AmaisPluginCatalog.DxpPageAnalyticsCode, entry.EndpointsByToolName["get_page_performance"]);
            Assert.Equal(AmaisPluginCatalog.DxpPageAnalyticsCode, entry.EndpointsByToolName["get_top_pages"]);
        }

        [Fact]
        public void GetOfferable_NoHeaderSent_StillExcludesPageAnalytics()
        {
            var result = AmaisPluginCatalog.GetOfferable(null, AmaisPluginCatalog.GetSkillSelectorCandidateNames(isDxpMcpServerConfigured: true));

            Assert.DoesNotContain(result, e => e.Name == AmaisPluginCatalog.PageAnalyticsName);
            Assert.Contains(result, e => e.Name == "Page");
        }

        [Fact]
        public void GetSkillSelectorCandidateNames_IncludesPageAnalyticsOnlyWhenDxpMcpConfigured()
        {
            Assert.DoesNotContain(AmaisPluginCatalog.PageAnalyticsName, AmaisPluginCatalog.GetSkillSelectorCandidateNames(false));
            Assert.Equal(
                ["Welcome", "Post", "Page", "GalleryFeature", "TextFeature", AmaisPluginCatalog.PageAnalyticsName],
                AmaisPluginCatalog.GetSkillSelectorCandidateNames(true).ToArray());
        }
    }
}
