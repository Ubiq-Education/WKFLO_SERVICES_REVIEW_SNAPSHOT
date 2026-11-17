using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Covers Task 6985: BuildAmaisAgentCapabilitiesInjection must feed the model a concrete list of the
    /// agent's actual tools/plugins (name + description) so capability questions ("what can you help
    /// with?") are answered from real configured tools instead of the generic system-prompt persona text.
    /// The caller (RunChatCompletionAgentAsync/RunVoiceChatCompletionAgentAsync) only invokes this when
    /// data.AmaisSkillsEnabled is true — see the *AmaisContextGatingTests files for that gating behavior.
    ///
    /// For an Integration_Amais tool, the skill selector loads Welcome/Post/Page/GalleryFeature/TextFeature
    /// into the kernel lazily per message, so kernel state can't be used to enumerate what the tenant's
    /// AMAIS integration could offer. Instead this reads the static AmaisPluginCatalog directly, gated by
    /// AreSkillsEnabled and the tenant's available AMAIS endpoints.
    /// </summary>
    public class SemanticKernelAgentServiceBuildAmaisAgentCapabilitiesInjectionTests
    {
        private static Agent CreateAgent(params AgentTool[] agentTools)
        {
            return new Agent
            {
                AgentTools = agentTools
            };
        }

        private static TenantTool CreateTool(int id, string name, string description, bool isActive = true, bool isDeleted = false, string type = null, string config = null)
        {
            return new TenantTool
            {
                Id = id,
                Name = name,
                Description = description,
                IsActive = isActive,
                IsDeleted = isDeleted,
                Type = type,
                Config = config
            };
        }

        private static ChatContext BuildChatContext(params string[] availableEndpoints) =>
            new() { AmaisAvailableEndpoints = availableEndpoints.Length > 0 ? availableEndpoints : null };

        [Fact]
        public void ActiveToolsWithDescriptions_ProducesHeaderAndBulletPerTool()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = CreateTool(1, "Knowledge Base", "Answers questions from the school handbook") },
                new AgentTool { Tool = CreateTool(2, "Contact Form", "Collects visitor contact details") });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("YOUR AVAILABLE TOOLS/PLUGINS:", result);
            Assert.Contains("- Knowledge Base: Answers questions from the school handbook", result);
            Assert.Contains("- Contact Form: Collects visitor contact details", result);
        }

        [Fact]
        public void ActiveToolWithoutDescription_FallsBackToNameOnlyBullet()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = CreateTool(1, "Image Generator", description: null) });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("- Image Generator", result);
            Assert.DoesNotContain("- Image Generator:", result);
        }

        [Fact]
        public void NoAgentTools_ReturnsEmptyString()
        {
            var agent = CreateAgent();

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void NullAgentTools_ReturnsEmptyString()
        {
            var agent = new Agent { AgentTools = null };

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void OnlyInactiveTools_ReturnsEmptyString()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = CreateTool(1, "Disabled Tool", "Should never appear", isActive: false) });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void OnlyDeletedTools_ReturnsEmptyString()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = CreateTool(1, "Deleted Tool", "Should never appear", isDeleted: true) });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void NullTool_IsSkipped()
        {
            var agent = CreateAgent(new AgentTool { Tool = null });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void NonRunableTool_IsSkipped()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = new MarketplaceTool { Id = 1, Name = "Marketplace Only", Description = "Not runnable" } });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void MixOfEligibleAndIneligibleTools_OnlyIncludesEligibleOnes()
        {
            var agent = CreateAgent(
                new AgentTool { Tool = CreateTool(1, "Active Tool", "Included") },
                new AgentTool { Tool = CreateTool(2, "Inactive Tool", "Excluded", isActive: false) },
                new AgentTool { Tool = CreateTool(3, "Deleted Tool", "Excluded", isDeleted: true) },
                new AgentTool { Tool = null });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("- Active Tool: Included", result);
            Assert.DoesNotContain("Inactive Tool", result);
            Assert.DoesNotContain("Deleted Tool", result);
        }

        [Fact]
        public void AmaisIntegration_SkillsEnabled_ListsFullCatalogInsteadOfToolNameAndDescription()
        {
            var agent = CreateAgent(
                new AgentTool
                {
                    Tool = CreateTool(1, "Amais Integration", "Generic tenant-facing description",
                        type: CommonConsts.CommonToolTypeConsts.IntegrationAmais,
                        config: "{\"areSkillsEnabled\":true}")
                });

            // Fail-open (no available-endpoints header) — every catalog entry is offerable.
            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("- Knowledge: Retrieves relevant school knowledge base passages to answer the user's question.", result);
            Assert.Contains("- Welcome: When user ask for welcome message.", result);
            Assert.Contains("- Post: Creates, clones, updates blog posts and manages post images.", result);
            Assert.Contains("- Page: Creates, clones, updates pages, retrieves page data, manages page images.", result);
            Assert.Contains("- GalleryFeature: Creates, updates, lists, and retrieves a specific gallery feature by ID, including its image renditions.", result);
            Assert.Contains("- TextFeature: Retrieves a specific feature's saved and published text content by feature ID.", result);
            Assert.DoesNotContain("Generic tenant-facing description", result);
        }

        [Fact]
        public void AmaisIntegration_SkillsDisabled_ListsOnlyKnowledge()
        {
            var agent = CreateAgent(
                new AgentTool
                {
                    Tool = CreateTool(1, "Amais Integration", "Generic tenant-facing description",
                        type: CommonConsts.CommonToolTypeConsts.IntegrationAmais,
                        config: "{\"areSkillsEnabled\":false}")
                });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("- Knowledge: Retrieves relevant school knowledge base passages to answer the user's question.", result);
            Assert.DoesNotContain("Welcome", result);
            Assert.DoesNotContain("Post", result);
            Assert.DoesNotContain("- Page", result);
            Assert.DoesNotContain("GalleryFeature", result);
            Assert.DoesNotContain("TextFeature", result);
        }

        [Fact]
        public void AmaisIntegration_MalformedConfig_FallsBackToKnowledgeOnlyWithoutThrowing()
        {
            var agent = CreateAgent(
                new AgentTool
                {
                    Tool = CreateTool(1, "Amais Integration", "Generic tenant-facing description",
                        type: CommonConsts.CommonToolTypeConsts.IntegrationAmais,
                        config: "{not valid json")
                });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Contains("- Knowledge:", result);
            Assert.DoesNotContain("Welcome", result);
        }

        [Fact]
        public void AmaisIntegration_SkillsEnabledButEndpointRestricted_OnlyListsOfferableEntries()
        {
            var agent = CreateAgent(
                new AgentTool
                {
                    Tool = CreateTool(1, "Amais Integration", "Generic tenant-facing description",
                        type: CommonConsts.CommonToolTypeConsts.IntegrationAmais,
                        config: "{\"areSkillsEnabled\":true}")
                });

            // Only the Welcome endpoint code is available — Post/Page/GalleryFeature/TextFeature should drop out.
            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext("WLC"));

            Assert.Contains("- Knowledge:", result);
            Assert.Contains("- Welcome:", result);
            Assert.DoesNotContain("- Post:", result);
            Assert.DoesNotContain("- Page:", result);
            Assert.DoesNotContain("- GalleryFeature:", result);
            Assert.DoesNotContain("- TextFeature:", result);
        }

        [Fact]
        public void InactiveAmaisIntegrationTool_IsExcludedEntirely()
        {
            var agent = CreateAgent(
                new AgentTool
                {
                    Tool = CreateTool(1, "Amais Integration", "Generic tenant-facing description",
                        isActive: false,
                        type: CommonConsts.CommonToolTypeConsts.IntegrationAmais,
                        config: "{\"areSkillsEnabled\":true}")
                });

            var result = SemanticKernelAgentService.BuildAmaisAgentCapabilitiesInjection(agent, BuildChatContext());

            Assert.Equal(string.Empty, result);
        }
    }
}
