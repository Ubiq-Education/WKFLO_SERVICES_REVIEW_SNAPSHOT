namespace AMAIS.WKFLO.Common.Helpers
{
    /// <summary>
    /// Model-family lookups shared by every code path that talks to a Claude model,
    /// regardless of whether it's hosted directly on Anthropic's API or on Azure via Microsoft Foundry.
    /// </summary>
    public static class AnthropicModelHelper
    {
        public static int GetMaxOutputTokens(string model) => model switch
        {
            // Claude Opus 4.6 — 128k
            var m when m.Contains("opus-4-6") => 128000,
            // Claude Opus 4.1, 4.0 (deprecated) — 32k
            var m when m.Contains("opus-4-1")
                    || m.Contains("opus-4-0")
                    || m.Contains("opus-4-20250514") => 32000,
            // Claude Opus 4.5, Sonnet 4.x, Haiku 4.x — 64k
            var m when m.Contains("opus-4")
                    || m.Contains("sonnet-4")
                    || m.Contains("haiku-4") => 64000,
            // Claude 3.7 — 64k
            var m when m.Contains("claude-3-7") => 64000,
            // Claude 3.5 — 8,192
            var m when m.Contains("claude-3-5") => 8192,
            // Claude 3 — 4,096
            var m when m.Contains("claude-3-") => 4096,
            _ => 8192
        };
    }
}
