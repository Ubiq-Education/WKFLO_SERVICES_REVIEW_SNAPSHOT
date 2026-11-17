using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System.Globalization;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Knowledge
{
    /// <summary>
    /// Extracts an optional local-calendar-date range from a user's message (resolved against up to the
    /// last 1-2 prior user turns for vague follow-ups), converting it into UTC instants used to filter
    /// event-dated knowledge in <see cref="Interfaces.Providers.IEisKnowledgeProvider"/>.
    /// </summary>
    /// <remarks>
    /// Replaces the old static keyword-based KnowledgeQueryIntentClassifier: ANY query can now carry
    /// time-context, not just a fixed "upcoming events"/"admissions" pair. This is a real LLM call (one
    /// extra round-trip per turn where knowledge retrieval fires) reusing the per-turn
    /// <see cref="IChatCompletionService"/> the same way
    /// <c>SemanticKernelAgentServiceClassify.RunChatClassificationAsync</c> does, with
    /// <see cref="FunctionChoiceBehavior.None()"/> set so it can never trigger tool-calling - mirrors
    /// <c>AmaisSkillSelectorPlugin</c>. Accepted trade-off: folding this into an existing classification
    /// call was considered and deliberately deferred as premature coupling (see PLAN.md's "Expansion"
    /// section) - revisit only if telemetry shows a real latency/cost problem.
    /// </remarks>
    public static class TimeContextExtractor
    {
        /// <summary>
        /// Hallucination guard: a date range this far from "now" (in either direction) is rejected outright
        /// rather than trusted, since a school chatbot has no legitimate reason to filter on dates further
        /// out than this.
        /// </summary>
        private const int SanityWindowYears = 3;

        public static async Task<(DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, bool? SortAscending)> ExtractDateRangeAsync(
            Kernel kernel,
            string userQuery,
            IReadOnlyList<string> recentUserTurns,
            DateTimeOffset asOfUtc,
            TimeZoneInfo timeZone,
            CancellationToken cancellationToken = default)
        {
            if (kernel == null || string.IsNullOrWhiteSpace(userQuery))
            {
                return (null, null, null);
            }

            timeZone ??= TimeZoneInfo.Utc;
            var logger = kernel.LoggerFactory.CreateLogger(nameof(TimeContextExtractor));
            var nowLocal = TimeZoneInfo.ConvertTime(asOfUtc, timeZone).DateTime;

            try
            {
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                var historyContext = recentUserTurns != null && recentUserTurns.Count > 0
                    ? string.Join(". ", recentUserTurns)
                    : "(none)";

                var prompt = $$"""
                    Determine whether the user's message refers to a specific calendar time period (a named
                    month, "last year", "next week", "upcoming", "in the past", an explicit date range, etc).

                    Today's local date is {{nowLocal:yyyy-MM-dd}} ({{nowLocal.DayOfWeek}}).

                    Recent conversation context (oldest first, may be "(none)"): {{historyContext}}

                    User's current message: {{userQuery}}

                    Respond ONLY with a single JSON object, no markdown fences, no explanation:
                    {"hasDateContext": true|false, "startDate": "yyyy-MM-dd"|null, "endDate": "yyyy-MM-dd"|null, "earliestOrLatest": "earliest"|"latest"|null}

                    Rules:
                    - hasDateContext is true only when a real, specific time period is being referred to -
                      either directly in the current message, or resolvable from the recent conversation
                      context for a vague follow-up (e.g. "what about before that?").
                    - Do NOT set hasDateContext to true, and do NOT invent a date range, just because the
                      message contains a vague recency word like "recent", "latest", or "new" with no actual
                      time period attached - leave both dates null and hasDateContext false in that case.
                      Recency preference is handled separately and does not need a date range.
                    - startDate/endDate are calendar dates only (yyyy-MM-dd), never a time of day.
                    - Future/"upcoming"/"next" phrasing with no explicit end: set startDate to today's date,
                      leave endDate null.
                    - Past/"previous"/already-happened phrasing with no explicit start: leave startDate null,
                      set endDate to today's date.
                    - If you cannot confidently resolve a real time period, respond with
                      {"hasDateContext": false, "startDate": null, "endDate": null, "earliestOrLatest": null}.
                    - earliestOrLatest: set to "earliest" ONLY when the user is asking for a single specific
                      chronologically-first item within the period - "the next event", "the first event of
                      September", "what's the next thing happening". Set to "latest" ONLY when asking for a
                      single chronologically-last item - "the most recent event", "the last event of the
                      year", "what was the latest thing that happened". Leave it null for a general listing
                      of what happened/is happening in a period (e.g. "what events are in August", "what
                      happened last month") even though such a listing still has a real date range - null
                      here means "no specific single extreme was requested", not "no date range".
                    - Examples: "what's the next event?" -> hasDateContext=true, startDate=today, endDate=null,
                      earliestOrLatest="earliest". "what was the last event that happened?" ->
                      hasDateContext=true, startDate=null, endDate=today, earliestOrLatest="latest". "what's
                      the first event of September?" -> hasDateContext=true, startDate/endDate spanning all of
                      September, earliestOrLatest="earliest". "what's the last event in September?" -> same
                      September range, earliestOrLatest="latest". "what happened in September?" -> same
                      September range, earliestOrLatest=null.
                    """;

                var history = new ChatHistory();
                history.AddSystemMessage("You are a precise date-range extractor for a school chatbot. Respond only with the exact JSON object requested - nothing else.");
                history.AddUserMessage(prompt);

                var settings = new PromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.None() };
                var response = await chatCompletionService.GetChatMessageContentsAsync(history, settings, kernel, cancellationToken);
                var rawContent = response?.FirstOrDefault()?.Content;

                return ParseAndConvert(rawContent, nowLocal, asOfUtc, timeZone, logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fails open for genuine extraction problems (malformed output, LLM/network error, timeout
                // surfaced as a non-cancellation exception) - proceeds without a date range, same as "no
                // time-context detected", so one failed extraction never fails the whole knowledge-retrieval
                // turn. Deliberately does NOT catch OperationCanceledException: swallowing a real
                // caller-requested cancellation here would let the turn silently continue into knowledge
                // retrieval/answer generation after the caller already gave up, wasting work instead of
                // honoring the cancellation - let it propagate normally instead.
                logger.LogWarning(ex, "TimeContextExtractor failed to extract a date range; proceeding without one");
                return (null, null, null);
            }
        }

        private static (DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, bool? SortAscending) ParseAndConvert(
            string content, DateTime nowLocal, DateTimeOffset asOfUtc, TimeZoneInfo timeZone, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return (null, null, null);
            }

            var trimmed = content.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var lines = trimmed.Split('\n');
                trimmed = string.Join("\n", lines.Skip(1).Take(Math.Max(lines.Length - 2, 0)));
            }

            TimeContextResponse parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<TimeContextResponse>(trimmed);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "TimeContextExtractor received malformed/non-JSON output: {Content}", content);
                return (null, null, null);
            }

            // Two-signal gate: a model leaking a plausible-looking date on an unrelated query must not
            // apply a filter unless it also explicitly claims the query has date context.
            if (parsed == null || !parsed.HasDateContext)
            {
                return (null, null, null);
            }

            var startDate = TryParseDate(parsed.StartDate);
            var endDate = TryParseDate(parsed.EndDate);

            if (startDate == null && endDate == null)
            {
                return (null, null, null);
            }

            if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
            {
                logger.LogWarning("TimeContextExtractor rejected an inverted range: start {Start} > end {End}", startDate, endDate);
                return (null, null, null);
            }

            var minDate = DateOnly.FromDateTime(nowLocal.AddYears(-SanityWindowYears));
            var maxDate = DateOnly.FromDateTime(nowLocal.AddYears(SanityWindowYears));

            if ((startDate.HasValue && (startDate.Value < minDate || startDate.Value > maxDate))
                || (endDate.HasValue && (endDate.Value < minDate || endDate.Value > maxDate)))
            {
                logger.LogWarning("TimeContextExtractor rejected an out-of-sanity-window range: {Start} - {End}", startDate, endDate);
                return (null, null, null);
            }

            // Only "earliest"/"latest" (case-insensitive) are recognized; anything else (typo, unexpected
            // casing variant the model invents, stray whitespace) degrades to null - no extremal sort
            // preference - rather than silently misapplying one.
            bool? sortAscending = parsed.EarliestOrLatest?.Trim().ToLowerInvariant() switch
            {
                "earliest" => true,
                "latest" => false,
                _ => null
            };

            var nowLocalDate = DateOnly.FromDateTime(nowLocal);

            DateTimeOffset? startUtc = null;
            if (startDate.HasValue)
            {
                var startLocalMidnight = startDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                var startInstant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocalMidnight, timeZone), TimeSpan.Zero);

                // Today-anchored ranges (e.g. "upcoming events") must keep instant-level precision so an
                // event that already ended earlier today is still excluded, rather than loosening to local
                // midnight. A genuinely past startDate is left untouched - clamping it to "now" would
                // invert a real past range.
                if (startDate.Value == nowLocalDate && startInstant < asOfUtc)
                {
                    startInstant = asOfUtc;
                }

                startUtc = startInstant;
            }

            DateTimeOffset? endUtc = null;
            if (endDate.HasValue)
            {
                // DST-safe: convert the wall-clock instant (next day's local midnight) to UTC first, then
                // subtract a tick in UTC - subtracting a tick in local time first could land on an
                // ambiguous/invalid local time across a DST transition.
                var endLocalNextMidnight = endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                var endInstant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocalNextMidnight, timeZone), TimeSpan.Zero) - TimeSpan.FromTicks(1);

                // Symmetric to the startUtc today-clamp above: a today-anchored end ("past events") must
                // not extend past the actual current instant, or an event scheduled later today (that
                // hasn't happened yet) would wrongly satisfy "event_end_date <= endUtc" and be returned as
                // already-past. A genuinely past endDate is left untouched - clamping it to "now" would
                // only ever narrow a real past range, never invert it.
                if (endDate.Value == nowLocalDate && endInstant > asOfUtc)
                {
                    endInstant = asOfUtc;
                }

                endUtc = endInstant;
            }

            return (startUtc, endUtc, sortAscending);
        }

        private static DateOnly? TryParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
        }

        private sealed class TimeContextResponse
        {
            [JsonProperty("hasDateContext")]
            public bool HasDateContext { get; set; }

            [JsonProperty("startDate")]
            public string StartDate { get; set; }

            [JsonProperty("endDate")]
            public string EndDate { get; set; }

            [JsonProperty("earliestOrLatest")]
            public string EarliestOrLatest { get; set; }
        }
    }
}
