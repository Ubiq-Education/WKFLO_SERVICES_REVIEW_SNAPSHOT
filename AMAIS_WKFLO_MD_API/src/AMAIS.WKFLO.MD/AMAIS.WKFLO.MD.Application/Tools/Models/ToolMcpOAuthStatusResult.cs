namespace AMAIS.WKFLO.MD.Application.Tools.Models
{
    public class ToolMcpOAuthStatusResult
    {
        /// <summary>One of <see cref="McpOAuthStatus"/>'s constants. A plain string (not an enum) to avoid
        /// any ambiguity in how it round-trips through JSON serialization.</summary>
        public string Status { get; set; }

        /// <summary>Set when <see cref="Status"/> is <see cref="McpOAuthStatus.ConnectionIssue"/> — the last
        /// recorded connection failure's message. Null otherwise.</summary>
        public string Reason { get; set; }

        /// <summary>When the token row was first created (the OAuth exchange first succeeding — not
        /// necessarily a working MCP handshake, see <see cref="McpOAuthStatus.ConnectionIssue"/>). Null when
        /// <see cref="Status"/> is <see cref="McpOAuthStatus.NotConnected"/>.</summary>
        public DateTimeOffset? ConnectedOnUtc { get; set; }
    }

    public static class McpOAuthStatus
    {
        /// <summary>No token has ever been saved for this tool (never connected, or disconnected).</summary>
        public const string NotConnected = "NotConnected";

        /// <summary>A token exists and either the last recorded connection attempt succeeded, or none has
        /// been recorded yet — see <c>ToolOAuthToken.LastCheckSucceeded</c>'s remarks for why null reads as healthy.</summary>
        public const string Connected = "Connected";

        /// <summary>A token exists but the last recorded real connection attempt (the MCP handshake, not just
        /// the OAuth exchange) failed — see <see cref="ToolMcpOAuthStatusResult.Reason"/>.</summary>
        public const string ConnectionIssue = "ConnectionIssue";
    }
}
