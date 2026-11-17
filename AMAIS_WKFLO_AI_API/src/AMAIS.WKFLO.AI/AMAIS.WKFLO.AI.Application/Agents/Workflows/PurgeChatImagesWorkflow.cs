using Dapr.Workflow;
using System.Globalization;

namespace AMAIS.WKFLO.AI.Application.Agents.Workflows
{
    /// <summary>
    /// Singleton, never-ending workflow that purges chat images at each tenant's local midnight (US 7974 / Task 7980).
    /// Every tick runs <see cref="PurgeChatImagesWorkflowActivity"/> (which decides which tenants are due), then sleeps on
    /// a durable timer until the top of the next UTC hour and continues as new. Hourly ticks are enough because every
    /// time zone offset in use is a whole multiple of 15 minutes and a tenant is due on the first tick of its new local
    /// date; tenants whose midnight is not on a UTC hour (e.g. UTC+05:30) are purged on the following tick.
    /// </summary>
    public class PurgeChatImagesWorkflow : Workflow<string, bool>
    {
        public override async Task<bool> RunAsync(WorkflowContext context, string input)
        {
            // The workflow's deterministic clock (replay-safe), passed so the activity evaluates the tick time.
            var tickUtc = DateTime.SpecifyKind(context.CurrentUtcDateTime, DateTimeKind.Utc);

            await context.CallActivityAsync(nameof(PurgeChatImagesWorkflowActivity), tickUtc.ToString("O", CultureInfo.InvariantCulture));

            await context.CreateTimer(DelayUntilNextTick(context.CurrentUtcDateTime));

            context.ContinueAsNew(input);
            return true;
        }

        /// <summary>How long to sleep from <paramref name="nowUtc"/> until <see cref="NextTickUtc"/> (always positive, at most 1 hour).</summary>
        public static TimeSpan DelayUntilNextTick(DateTime nowUtc) => NextTickUtc(nowUtc) - DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

        /// <summary>The top of the UTC hour after <paramref name="nowUtc"/> (always later than it).</summary>
        public static DateTime NextTickUtc(DateTime nowUtc)
        {
            var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        }
    }
}
