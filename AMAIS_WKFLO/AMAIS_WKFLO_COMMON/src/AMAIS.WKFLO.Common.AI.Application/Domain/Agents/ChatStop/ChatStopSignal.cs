using System.Text.Json;
using System.Text.Json.Serialization;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop
{
    /// <summary>
    /// US 7501: constants of the Stop feature, shared by the state store, the broadcast and the activities.
    /// </summary>
    public static class ChatStopConsts
    {
        /// <summary>Dapr state key prefix: <c>chat-stop-{tenant}:{sessionId}</c> holds the last <see cref="ChatStopSignal.StoppedAt"/>.</summary>
        public const string StateKeyPrefix = "chat-stop-";

        /// <summary>
        /// The stop key only has to outlive the turns that started before it. A turn that is still "processing" after
        /// 5 minutes is already treated as stale, so one hour is generous.
        /// </summary>
        public const int StateTtlSeconds = 3600;

        /// <summary>
        /// A Stop is scoped by (tenant, session): a session id is chosen by the client, so it never identifies a chat on
        /// its own. Tenant names are compared case-insensitively.
        /// </summary>
        public static string BuildStateKey(string tenantName, string sessionId) => $"{StateKeyPrefix}{BuildScope(tenantName, sessionId)}";

        /// <summary>The (tenant, session) scope of a Stop, as one string: <c>{tenant}:{sessionId}</c>.</summary>
        public static string BuildScope(string tenantName, string sessionId) => $"{NormalizeTenant(tenantName)}:{sessionId}";

        public static string NormalizeTenant(string tenantName) => (tenantName ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// US 7501: "the user pressed Stop in this session of this tenant at <see cref="StoppedAt"/>". Every turn of that
    /// (tenant, session) that started before that instant is cancelled; turns started later (the user's next message)
    /// are not, and nothing of another tenant is ever touched.
    /// </summary>
    public sealed record ChatStopSignal(
        [property: JsonPropertyName("tenantName")] string TenantName,
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("stoppedAt")] DateTimeOffset StoppedAt)
    {
        public string ToJson() => JsonSerializer.Serialize(this);

        /// <summary>Parses a broadcast payload. Returns false for anything malformed instead of throwing.</summary>
        public static bool TryParse(string json, out ChatStopSignal signal)
        {
            signal = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                signal = JsonSerializer.Deserialize<ChatStopSignal>(json);
            }
            catch (JsonException)
            {
                return false;
            }

            return signal != null && !string.IsNullOrWhiteSpace(signal.TenantName) && !string.IsNullOrWhiteSpace(signal.SessionId) && signal.StoppedAt != default;
        }
    }
}
