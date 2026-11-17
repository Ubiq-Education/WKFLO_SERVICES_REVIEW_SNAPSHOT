namespace AMAIS.WKFLO.DXP.MCP.Application.Pages
{
    /// <summary>
    /// Inclusive range of calendar days (UTC) sent to the Report API as <c>DateFrom</c>/<c>DateTo</c>.
    /// The Report API treats <c>DateTo</c> as inclusive (it queries up to the start of the following day).
    /// </summary>
    public readonly record struct DxpDateWindow(DateTime From, DateTime To)
    {
        /// <summary>
        /// Same window the Report API builds for <c>LastDays = n</c>: from <c>today - n</c> up to and including today.
        /// </summary>
        public static DxpDateWindow ForLastDays(DateTime utcNow, int lastDays)
        {
            var today = utcNow.Date;
            return new DxpDateWindow(today.AddDays(-lastDays), today);
        }

        public int LengthInDays => (To - From).Days + 1;

        /// <summary>
        /// The window of the same length that ends the day before this one starts.
        /// </summary>
        public DxpDateWindow Previous() => new(From.AddDays(-LengthInDays), From.AddDays(-1));
    }
}
