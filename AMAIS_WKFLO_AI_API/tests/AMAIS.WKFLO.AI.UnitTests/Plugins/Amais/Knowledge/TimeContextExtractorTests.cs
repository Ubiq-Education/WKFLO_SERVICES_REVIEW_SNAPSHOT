using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Plugins.Amais.Knowledge
{
    /// <summary>
    /// Mock-based coverage of TimeContextExtractor, following the same Kernel.CreateBuilder() + mocked
    /// IChatCompletionService pattern already used by SemanticKernelExtensionsAutoInvokeAmaisKnowledgeAsyncTests
    /// and AmaisSkillSelectorPluginTests.
    /// </summary>
    public class TimeContextExtractorTests
    {
        private static Kernel BuildKernel(string rawResponse = null, Exception exception = null)
        {
            var builder = Kernel.CreateBuilder();
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();

            if (exception != null)
            {
                chatCompletionServiceMock
                    .Setup(x => x.GetChatMessageContentsAsync(
                        It.IsAny<ChatHistory>(),
                        It.IsAny<PromptExecutionSettings>(),
                        It.IsAny<Kernel>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(exception);
            }
            else
            {
                IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, rawResponse)];
                chatCompletionServiceMock
                    .Setup(x => x.GetChatMessageContentsAsync(
                        It.IsAny<ChatHistory>(),
                        It.IsAny<PromptExecutionSettings>(),
                        It.IsAny<Kernel>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(response);
            }

            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            return builder.Build();
        }

        private static Kernel BuildKernelCapturingHistory(string rawResponse, out Func<ChatHistory> capturedHistory)
        {
            var builder = Kernel.CreateBuilder();
            var chatCompletionServiceMock = new Mock<IChatCompletionService>();
            ChatHistory captured = null;

            IReadOnlyList<ChatMessageContent> response = [new ChatMessageContent(AuthorRole.Assistant, rawResponse)];
            chatCompletionServiceMock
                .Setup(x => x.GetChatMessageContentsAsync(
                    It.IsAny<ChatHistory>(),
                    It.IsAny<PromptExecutionSettings>(),
                    It.IsAny<Kernel>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ChatHistory, PromptExecutionSettings, Kernel, CancellationToken>((history, _, _, _) => captured = history)
                .ReturnsAsync(response);

            builder.Services.AddSingleton(chatCompletionServiceMock.Object);
            capturedHistory = () => captured;
            return builder.Build();
        }

        private static readonly DateTimeOffset FixedAsOfUtc = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        // ── Null/empty query short-circuit ──────────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_NullQuery_ReturnsNullRangeWithoutCallingLlm()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, null, null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EmptyQuery_ReturnsNullRange()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, string.Empty, null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_WhitespaceQuery_ReturnsNullRange()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "   ", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        // ── Happy path with correct UTC conversion ──────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_UpcomingEventsStyle_ConvertsStartDateToUtcMidnight()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-07-30\", \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what upcoming events do you have?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero), start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_BothDatesPresent_ConvertsBothBounds()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-06-01\", \"endDate\": \"2026-06-30\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "events in June", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), start);
            // End is local midnight of endDate+1 day minus one tick.
            Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1), end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_NonUtcTimeZone_ConvertsUsingThatZonesOffset()
        {
            // "Eastern Standard Time" (a fixed, non-DST-transition date: mid-January is EST, UTC-5).
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-20\", \"endDate\": null}");

            var (start, _, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "upcoming events", null, asOfUtc, easternZone, TestContext.Current.CancellationToken);

            // Local midnight (00:00 EST, UTC-5) => 05:00 UTC.
            Assert.Equal(new DateTimeOffset(2026, 1, 20, 5, 0, 0, TimeSpan.Zero), start);
        }

        // ── DST-transition-day case ──────────────────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_EndDateOnDstSpringForwardTransitionDay_UsesPostTransitionOffsetForNextMidnight()
        {
            // US Eastern DST for 2026 begins Sunday, March 8th (clocks jump 2am -> 3am, EST -> EDT).
            // endDate = 2026-03-08: the boundary is local midnight of the NEXT day (2026-03-09 00:00),
            // which already falls after the transition, so it must use the EDT (UTC-4) offset, not EST.
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-03-08\"}");

            var (_, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "events up to March 8th", null, asOfUtc, easternZone, TestContext.Current.CancellationToken);

            // 2026-03-09T00:00 EDT (UTC-4) => 2026-03-09T04:00:00Z, minus one tick.
            var expected = new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero).AddTicks(-1);
            Assert.Equal(expected, end);
        }

        // ── earliestOrLatest extremal preference ────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestEarliest_ReturnsSortAscendingTrue()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null, \"earliestOrLatest\": \"earliest\"}");

            var (_, _, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what's the next event?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.True(sortAscending);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestLatest_ReturnsSortAscendingFalse()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-01-01\", \"earliestOrLatest\": \"latest\"}");

            var (_, _, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what was the last event that happened?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.False(sortAscending);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestNull_ReturnsSortAscendingNull()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-09-01\", \"endDate\": \"2026-09-30\", \"earliestOrLatest\": null}");

            var (_, _, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what happened in September?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(sortAscending);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestWithinClosedRange_ReturnsSortAscendingTrue()
        {
            // "first event of September": both bounds set (a closed range), but the user still wants a
            // single specific extreme within it, not a general listing.
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-09-01\", \"endDate\": \"2026-09-30\", \"earliestOrLatest\": \"earliest\"}");

            var (start, end, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what's the first event of September?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.NotNull(start);
            Assert.NotNull(end);
            Assert.True(sortAscending);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestUnrecognizedValue_DegradesToNullRatherThanThrowing()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null, \"earliestOrLatest\": \"soonest\"}");

            var (_, _, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what's next?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(sortAscending);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EarliestOrLatestMissingFromResponse_ReturnsSortAscendingNull()
        {
            // Backward-compat: a response that omits the field entirely (e.g. an older cached prompt
            // response) must not throw and must degrade to "no extremal preference".
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-01-01\", \"endDate\": null}");

            var (start, _, sortAscending) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "upcoming events", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.NotNull(start);
            Assert.Null(sortAscending);
        }

        // ── Two-signal gate ──────────────────────────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_HasDateContextFalseWithNonNullDates_ReturnsNullRange()
        {
            var kernel = BuildKernel("{\"hasDateContext\": false, \"startDate\": \"2026-01-01\", \"endDate\": \"2026-01-31\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "recent newsletter", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_HasDateContextTrueWithBothDatesNull_ReturnsNullRange()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "tell me something", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        // ── Defensive fallback: malformed/non-JSON/markdown-fenced/empty/exception/cancellation ──

        [Fact]
        public async Task ExtractDateRangeAsync_MalformedJson_FallsBackToNullRange()
        {
            var kernel = BuildKernel("this is not json at all");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_NonJsonPlainTextResponse_FallsBackToNullRange()
        {
            var kernel = BuildKernel("Sure! There are no upcoming events.");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_MarkdownFencedValidJson_ParsesSuccessfully()
        {
            var kernel = BuildKernel("```json\n{\"hasDateContext\": true, \"startDate\": \"2026-02-01\", \"endDate\": null}\n```");
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "events in February", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_MarkdownFencedMalformedJson_FallsBackToNullRange()
        {
            var kernel = BuildKernel("```json\nnot actually json\n```");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EmptyResponseContent_FallsBackToNullRange()
        {
            var kernel = BuildKernel(string.Empty);

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_NullResponseContent_FallsBackToNullRange()
        {
            var kernel = BuildKernel(rawResponse: null);

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_ChatCompletionThrows_FallsBackToNullRangeWithoutThrowing()
        {
            var kernel = BuildKernel(exception: new InvalidOperationException("LLM unavailable"));

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_ChatCompletionCancelled_PropagatesCancellationRatherThanSwallowingIt()
        {
            // Regression for a Copilot PR-review finding: swallowing OperationCanceledException here made
            // a real caller-requested cancellation look identical to "no date context found", letting the
            // turn silently continue into knowledge retrieval/answer generation after the caller already
            // gave up. Only genuine extraction failures (malformed output, non-cancellation exceptions)
            // should fail open - cancellation must propagate normally.
            var kernel = BuildKernel(exception: new OperationCanceledException());

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task ExtractDateRangeAsync_NonCancellationException_StillFallsBackToNullRangeWithoutThrowing()
        {
            var kernel = BuildKernel(exception: new InvalidOperationException("simulated LLM failure"));

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        // ── startDate > endDate rejected outright ───────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_StartAfterEnd_RejectsWithoutAutoSwap()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-06-30\", \"endDate\": \"2026-06-01\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "some query", null, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        // ── Out-of-sanity-window dates rejected ─────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_StartDateFarInFuture_RejectsAsHallucination()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2099-01-01\", \"endDate\": null}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "far future events", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EndDateFarInPast_RejectsAsHallucination()
        {
            var asOfUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"1990-01-01\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "ancient events", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Null(end);
        }

        // ── Today-clamp behavior ─────────────────────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_StartDateIsToday_ClampsToAsOfUtcInstantNotLocalMidnight()
        {
            // "now" is mid-afternoon today; an "upcoming events" style response resolves startDate to
            // today's local date. The start bound must be clamped up to the exact "now" instant so an
            // event that already ended earlier today is still excluded.
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-07-30\", \"endDate\": null}");

            var (start, _, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what upcoming events do you have?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(asOfUtc, start);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_StartDateInRealPast_IsNotClampedToNow()
        {
            // A genuinely past start date (e.g. "events since last month") must be left as real local
            // midnight, not clamped up to "now" - clamping here would invert the range.
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": \"2026-06-01\", \"endDate\": null}");

            var (start, _, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "events since last month", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), start);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EndDateIsToday_ClampsToAsOfUtcInstantNotLocalEndOfDay()
        {
            // Regression for a Copilot PR-review finding, symmetric to the startUtc today-clamp above:
            // "now" is mid-afternoon today; a "past events" style response resolves endDate to today's
            // local date. Without a clamp, the end bound would extend to local end-of-day, so an event
            // later today (that hasn't happened yet) would wrongly satisfy "event_end_date <= end" and be
            // returned as already-past. Must clamp down to the exact "now" instant instead.
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-07-30\"}");

            var (_, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "what happened so far today?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(asOfUtc, end);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_EndDateInRealPast_IsNotClampedToNow()
        {
            // A genuinely past end date (e.g. "events up to last month") must be left as real local
            // end-of-day, not clamped down to "now" - clamping here would only ever be a no-op at best,
            // but must never fire for a date that isn't actually today.
            var asOfUtc = new DateTimeOffset(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-06-01\"}");

            var (_, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "events up to last month", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Equal(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(-1), end);
        }

        // ── One-sided "past" range ───────────────────────────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_PastEventsStyle_OnlySetsEndBound()
        {
            // endDate is genuinely in the past relative to asOfUtc (not "today") so this test exercises the
            // plain end-of-local-day conversion in isolation - the today-clamp case is covered separately
            // above (ExtractDateRangeAsync_EndDateIsToday_ClampsToAsOfUtcInstantNotLocalEndOfDay).
            var asOfUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-07-30\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "were there some in the past?", null, asOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.Equal(new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero).AddTicks(-1), end);
        }

        // ── Multi-turn follow-up using passed history ───────────────────────

        [Fact]
        public async Task ExtractDateRangeAsync_WithRecentUserTurns_IncludesThemInThePromptSentToTheModel()
        {
            var kernel = BuildKernelCapturingHistory(
                "{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-05-01\"}",
                out var capturedHistory);
            var recentTurns = new List<string> { "What upcoming events do you have?", "Were there some in the past?" };

            await TimeContextExtractor.ExtractDateRangeAsync(kernel, "Were there some in the past?", recentTurns, FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            var promptMessage = capturedHistory().Last(m => m.Role == AuthorRole.User);
            Assert.Contains("What upcoming events do you have?", promptMessage.Content);
        }

        [Fact]
        public async Task ExtractDateRangeAsync_NoRecentUserTurns_StillWorksWithoutThrowing()
        {
            var kernel = BuildKernel("{\"hasDateContext\": true, \"startDate\": null, \"endDate\": \"2026-05-01\"}");

            var (start, end, _) = await TimeContextExtractor.ExtractDateRangeAsync(kernel, "past events", new List<string>(), FixedAsOfUtc, TimeZoneInfo.Utc, TestContext.Current.CancellationToken);

            Assert.Null(start);
            Assert.NotNull(end);
        }
    }
}
