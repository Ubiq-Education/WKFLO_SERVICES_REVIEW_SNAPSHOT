using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// Covers Task 6985 follow-up: knowledge injected by AutoInvokeAmaisKnowledgeAsync lands in chat
    /// history right before the current turn, so on recency alone it could outweigh the tools/capabilities
    /// list baked into the agent's Instructions. It must be framed as subordinate background info that
    /// defers to the tools/plugins list for capability questions.
    /// </summary>
    public class SemanticKernelExtensionsAutoInvokeAmaisKnowledgeAsyncTests
    {
        private const string NoDateContextResponse = "{\"hasDateContext\": false, \"startDate\": null, \"endDate\": null}";

        private static Kernel BuildKernelWithKnowledgePlugin(IEisKnowledgeProvider eisKnowledgeProvider, string timeContextRawResponse = NoDateContextResponse)
        {
            var builder = Kernel.CreateBuilder();

            builder.Services.AddSingleton(eisKnowledgeProvider);
            builder.Services.AddSingleton(Mock.Of<IChatNotificationService>());
            builder.Services.AddSingleton(new TenantContext());
            builder.Services.AddSingleton(new ChatContext());

            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, timeContextRawResponse)];
            chatCompletionServiceMock
                .Setup(x => x.GetChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            builder.Services.AddSingleton(chatCompletionServiceMock.Object);

            var kernel = builder.Build();
            kernel.Plugins.AddFromType<AmaisKnowledgePlugin>("Knowledge", kernel.Services);
            return kernel;
        }

        private static Mock<IEisKnowledgeProvider> BuildProviderReturning(params EisDocument[] docs)
        {
            var provider = new Mock<IEisKnowledgeProvider>();
            provider
                .Setup(p => p.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EisKnowledgeQueryOptions>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(docs);
            return provider;
        }

        [Fact]
        public async Task KnowledgeFound_MessageIsFramedAsSubordinateBackgroundInfo()
        {
            var provider = BuildProviderReturning(new EisDocument { Name = "About Us", Content = "We offer...", Links = "{}" });
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, "what can you help with?", Mock.Of<IDateTimeProvider>());

            var message = Assert.Single(chatHistory);
            Assert.Equal(AuthorRole.System, message.Role);
            Assert.StartsWith(
                "Background knowledge (only relevant to specific factual questions — do not use this to describe your capabilities; defer to your tools/plugins list for that).",
                message.Content);
            Assert.Contains("We offer...", message.Content);
        }

        [Fact]
        public async Task KnowledgeFound_WithNonUtcSchoolTimeZone_AddsTimeZoneConversionInstruction()
        {
            var provider = BuildProviderReturning(new EisDocument { Name = "About Us", Content = "We offer...", Links = "{}" });
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();
            var chatContext = new ChatContext { TimeZone = "Pacific Standard Time" };

            await kernel.AutoInvokeAmaisKnowledgeAsync(chatContext, chatHistory, "what can you help with?", Mock.Of<IDateTimeProvider>());

            var message = Assert.Single(chatHistory);
            Assert.Contains(
                "All dates/times in the passages below are in UTC - the school's local timezone is Pacific Standard Time.",
                message.Content);
        }

        [Fact]
        public async Task KnowledgeFound_WithNonUtcSchoolTimeZone_DefersPerDateDaylightJudgmentToModel()
        {
            // Regression for a Copilot PR-review finding: the note previously precomputed a single
            // DST-aware label from "now" (e.g. "Pacific Daylight Time" in September) and applied it to
            // every passage uniformly - but a retrieved passage can carry a timestamp from a different
            // month (e.g. a January event referenced in a September conversation), whose correct
            // standard/daylight status depends on ITS OWN date, not today's. The note must name the zone
            // and defer the per-timestamp judgment to the model instead of asserting one label for all.
            var provider = BuildProviderReturning(new EisDocument { Name = "About Us", Content = "We offer...", Links = "{}" });
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();
            var chatContext = new ChatContext { TimeZone = "Pacific Standard Time" };
            var asOfUtc = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero); // September: PDT in effect "now".
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            dateTimeProviderMock.Setup(x => x.DateTimeOffsetUtcNow()).Returns(asOfUtc);

            await kernel.AutoInvokeAmaisKnowledgeAsync(chatContext, chatHistory, "what can you help with?", dateTimeProviderMock.Object);

            var message = Assert.Single(chatHistory);
            // No hardcoded "Pacific Daylight Time"/"Pacific Standard Time" label anywhere - the instruction
            // names the zone once and tells the model daylight-vs-standard depends on the date being
            // converted, not on "now".
            Assert.DoesNotContain("Pacific Daylight Time", message.Content);
            Assert.DoesNotContain("Pacific Standard Time)", message.Content); // not embedded as a parenthetical label
            Assert.Contains("for THAT SPECIFIC DATE", message.Content);
            Assert.Contains("not on today's date", message.Content);
        }

        [Fact]
        public async Task KnowledgeFound_WithNoSchoolTimeZone_OmitsTimeZoneConversionInstruction()
        {
            var provider = BuildProviderReturning(new EisDocument { Name = "About Us", Content = "We offer...", Links = "{}" });
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, "what can you help with?", Mock.Of<IDateTimeProvider>());

            var message = Assert.Single(chatHistory);
            Assert.DoesNotContain("the school's local timezone is", message.Content);
        }

        [Fact]
        public async Task NoRelevantKnowledge_FallbackTextIsStillFramed()
        {
            var provider = BuildProviderReturning();
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, "what can you help with?", Mock.Of<IDateTimeProvider>());

            var message = Assert.Single(chatHistory);
            Assert.StartsWith("Background knowledge", message.Content);
            Assert.Contains("No relevant knowledge base passages were found", message.Content);
        }

        [Fact]
        public async Task EmptyUserQuery_NoMessageAdded()
        {
            var provider = BuildProviderReturning(new EisDocument { Name = "About Us", Content = "We offer...", Links = "{}" });
            var kernel = BuildKernelWithKnowledgePlugin(provider.Object);
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, string.Empty, Mock.Of<IDateTimeProvider>());

            Assert.Empty(chatHistory);
        }

        [Fact]
        public async Task NoKnowledgePluginRegistered_NoMessageAdded()
        {
            var kernel = Kernel.CreateBuilder().Build();
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, "what can you help with?", Mock.Of<IDateTimeProvider>());

            Assert.Empty(chatHistory);
        }

        [Fact]
        public async Task WithDateContext_PassesExtractedRangeThroughToProvider()
        {
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(d => d.DateTimeOffsetUtcNow()).Returns(asOfUtc);

            var provider = BuildProviderReturning();
            var kernel = BuildKernelWithKnowledgePlugin(
                provider.Object,
                "{\"hasDateContext\": true, \"startDate\": \"2026-07-30\", \"endDate\": null}");
            var chatHistory = new ChatHistory();

            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext { TimeZone = "UTC" }, chatHistory, "what upcoming events do you have?", dateTimeProvider.Object);

            provider.Verify(p => p.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<EisKnowledgeQueryOptions>(o => o.EventDateRangeStartUtc == asOfUtc && o.EventDateRangeEndUtc == null),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WithoutTimeZoneHeader_ResolvesTimeZoneToUtc()
        {
            var asOfUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new Mock<IDateTimeProvider>();
            dateTimeProvider.Setup(d => d.DateTimeOffsetUtcNow()).Returns(asOfUtc);

            var provider = BuildProviderReturning();
            var kernel = BuildKernelWithKnowledgePlugin(
                provider.Object,
                "{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-03-01\"}");
            var chatHistory = new ChatHistory();

            // No TimeZone set on ChatContext at all — must fail open to UTC, not throw.
            await kernel.AutoInvokeAmaisKnowledgeAsync(new ChatContext(), chatHistory, "what happened recently?", dateTimeProvider.Object);

            provider.Verify(p => p.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<EisKnowledgeQueryOptions>(o => o.EventDateRangeEndUtc != null),
                It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
