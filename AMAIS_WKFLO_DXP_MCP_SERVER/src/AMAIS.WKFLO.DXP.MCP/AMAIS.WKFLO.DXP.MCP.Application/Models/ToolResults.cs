using AMAIS.WKFLO.DXP.MCP.Application.Pages;

namespace AMAIS.WKFLO.DXP.MCP.Application.Models
{
    public sealed record DxpPeriod(string From, string To, int Days)
    {
        public static DxpPeriod FromWindow(DxpDateWindow window) =>
            new(window.From.ToString("yyyy-MM-dd"), window.To.ToString("yyyy-MM-dd"), window.LengthInDays);
    }

    public sealed record TopPagesResult(DxpPeriod Period, IReadOnlyList<PageMetrics> Pages);

    public sealed record PagePerformanceResult(
        string? PageId,
        string? Path,
        string? Url,
        bool Found,
        DxpPeriod Period,
        PageMetrics? Current,
        DxpPeriod PreviousPeriod,
        PageMetrics? Previous,
        string? Note);
}
