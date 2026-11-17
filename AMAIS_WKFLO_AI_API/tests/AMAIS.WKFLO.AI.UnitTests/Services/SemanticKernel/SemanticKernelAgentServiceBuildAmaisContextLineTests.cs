using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers Task 6847: BuildAmaisContextLine must always emit the context line and always state the current
    /// page alias / current feature ID explicitly, falling back to the literal value "NONE" when either is
    /// unset, so the model always has an unambiguous signal instead of a silently omitted bullet or an
    /// empty context line.
    ///
    /// Task 7015: the method was renamed from BuildContextLine to BuildAmaisContextLine to make its Amais-specific
    /// nature explicit, and its invocation is now gated behind the AmaisSkillsEnabled flag at the call sites
    /// (see RunChatCompletionAgentAsyncAmaisContextGatingTests / RunVoiceChatCompletionAgentAsyncAmaisContextGatingTests
    /// for coverage of the gating behavior itself). This file continues to cover only the pure string-building
    /// function, unaffected by the gating.
    /// </summary>
    public class SemanticKernelAgentServiceBuildAmaisContextLineTests
    {
        [Fact]
        public void BuildAmaisContextLine_BothPageAndFeaturePresent_StatesBothValuesAndKeepsReferenceInstructions()
        {
            var chatContext = new ChatContext
            {
                PagePath = "/pricing",
                FeatureId = "feature-123",
                FeatureType = "Text",
                FeatureName = "Hero Banner"
            };

            var result = SemanticKernelAgentService.BuildAmaisContextLine(chatContext);

            Assert.Contains("- Current page alias: pricing", result);
            Assert.Contains("- Current feature ID: feature-123", result);
            Assert.Contains("- Current feature type: Text", result);
            Assert.Contains("- Current feature name: Hero Banner", result);
            Assert.Contains("Always reference this page alias and feature ID for any related queries", result);
            Assert.Contains("CRITICAL - FEATURE DATA HANDLING:", result);
        }

        [Fact]
        public void BuildAmaisContextLine_OnlyPagePresent_FeatureBulletReadsNone()
        {
            var chatContext = new ChatContext
            {
                PagePath = "/about-us",
                FeatureId = null
            };

            var result = SemanticKernelAgentService.BuildAmaisContextLine(chatContext);

            Assert.Contains("- Current page alias: about-us", result);
            Assert.Contains("- Current feature ID: NONE", result);
            Assert.DoesNotContain("CRITICAL - FEATURE DATA HANDLING:", result);
            Assert.Contains("Always reference this page alias for any related queries", result);
        }

        [Fact]
        public void BuildAmaisContextLine_OnlyFeaturePresent_PageBulletReadsNone()
        {
            var chatContext = new ChatContext
            {
                PagePath = null,
                FeatureId = "feature-456",
                FeatureType = "Gallery",
                FeatureName = "Photo Gallery"
            };

            var result = SemanticKernelAgentService.BuildAmaisContextLine(chatContext);

            Assert.Contains("- Current page alias: NONE", result);
            Assert.Contains("- Current feature ID: feature-456", result);
            Assert.Contains("- Current feature type: Gallery", result);
            Assert.Contains("- Current feature name: Photo Gallery", result);
            Assert.Contains("Always reference this feature ID for any related queries", result);
        }

        [Fact]
        public void BuildAmaisContextLine_NeitherPresent_BothBulletsReadNoneAndMethodDoesNotReturnEmpty()
        {
            var chatContext = new ChatContext
            {
                PagePath = null,
                FeatureId = null
            };

            var result = SemanticKernelAgentService.BuildAmaisContextLine(chatContext);

            Assert.NotEqual(string.Empty, result);
            Assert.Contains("- Current page alias: NONE", result);
            Assert.Contains("- Current feature ID: NONE", result);
            Assert.DoesNotContain("Always reference this", result);
            Assert.DoesNotContain("CRITICAL - FEATURE DATA HANDLING:", result);
        }

        [Fact]
        public void BuildAmaisContextLine_PageAnalyticsAvailableWithPage_MentionsPerformanceToolWithCurrentPath()
        {
            var chatContext = new ChatContext { PagePath = "/admissions" };

            var result = SemanticKernelAgentService.BuildAmaisContextLine(chatContext, pageAnalyticsAvailable: true);

            Assert.Contains("use the Page Performance tool with page path /admissions", result);
            Assert.Contains("Only fetch analytics when the user asks for them", result);
        }

        [Fact]
        public void BuildAmaisContextLine_PageAnalyticsAvailableWithoutPage_MentionsToolsWithoutPath()
        {
            var result = SemanticKernelAgentService.BuildAmaisContextLine(new ChatContext(), pageAnalyticsAvailable: true);

            Assert.Contains("Page Performance and Top Pages tools", result);
            Assert.DoesNotContain("page path", result);
        }

        [Fact]
        public void BuildAmaisContextLine_PageAnalyticsUnavailable_DoesNotMentionAnalytics()
        {
            var result = SemanticKernelAgentService.BuildAmaisContextLine(new ChatContext { PagePath = "/admissions" });

            Assert.DoesNotContain("Page Performance", result);
        }
    }
}
