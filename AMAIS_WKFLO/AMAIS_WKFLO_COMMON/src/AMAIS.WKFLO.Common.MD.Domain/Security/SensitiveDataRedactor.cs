using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.MD.Domain.Security
{
    /// <summary>
    /// Marks a request whose <c>code</c>/<c>state</c> properties are an OAuth authorization callback's one-time
    /// authorization code and CSRF state. <see cref="SensitiveDataRedactor"/> masks those two names only for
    /// requests carrying this marker — elsewhere "code"/"state" are ordinary, readable fields (error codes,
    /// workflow states).
    /// </summary>
    public interface IOAuthCallbackRequest
    {
    }

    /// <summary>
    /// Masks sensitive values in serialized JSON before it is logged or audited: any property whose name is in
    /// the sensitive set (case-insensitive, exact name match) gets its non-empty value replaced with
    /// <see cref="RedactedValue"/> — at any depth, including inside JSON carried as a *string* property (a tool
    /// command's <c>Config</c>). Text that isn't JSON is masked with a pattern fallback that understands both
    /// the plain and the escaped-quote form. Text that never mentions a sensitive name is returned untouched
    /// without being parsed.
    /// </summary>
    /// <remarks>
    /// Two name sets, by design: <see cref="AlwaysSensitiveNames"/> are unambiguous secret names, safe to mask
    /// everywhere; <see cref="OAuthCallbackNames"/> (<c>code</c>, <c>state</c>) are generic words that also name
    /// ordinary fields (e.g. <c>Error.Code</c> in every result), so they are added only for requests marked
    /// <see cref="IOAuthCallbackRequest"/>. A marker interface keeps that opt-in next to the command it protects
    /// and is picked up by the one shared logging behaviour — no per-call-site redaction.
    /// </remarks>
    public static class SensitiveDataRedactor
    {
        public const string RedactedValue = "***";

        public static readonly IReadOnlyList<string> AlwaysSensitiveNames =
        [
            "clientSecret", "client_secret",
            "codeVerifier", "code_verifier",
            "accessToken", "access_token",
            "refreshToken", "refresh_token"
        ];

        public static readonly IReadOnlyList<string> OAuthCallbackNames = ["code", "state"];

        private static readonly JsonSerializerSettings ParseSettings = new() { DateParseHandling = DateParseHandling.None };

        private static readonly NameSet Default = new(AlwaysSensitiveNames);
        private static readonly NameSet WithOAuthCallback = new([.. AlwaysSensitiveNames, .. OAuthCallbackNames]);

        /// <summary>Masks <see cref="AlwaysSensitiveNames"/> in <paramref name="text"/>.</summary>
        public static string Redact(string text) => Redact(text, Default);

        /// <summary>
        /// Masks the names appropriate for <paramref name="message"/>: <see cref="AlwaysSensitiveNames"/>, plus
        /// <see cref="OAuthCallbackNames"/> when it is an <see cref="IOAuthCallbackRequest"/>.
        /// </summary>
        public static string RedactFor(object message, string text) =>
            Redact(text, message is IOAuthCallbackRequest ? WithOAuthCallback : Default);

        private static string Redact(string text, NameSet names)
        {
            if (string.IsNullOrEmpty(text) || !names.MentionedIn(text))
                return text;

            if (TryParseToken(text, out var token))
            {
                RedactToken(token, names);
                return token.ToString(Formatting.None);
            }

            return names.RedactWithPatterns(text);
        }

        private static void RedactToken(JToken token, NameSet names)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var property in obj.Properties())
                    {
                        if (names.Contains(property.Name))
                        {
                            if (property.Value.Type != JTokenType.Null &&
                                !(property.Value.Type == JTokenType.String && string.IsNullOrEmpty(property.Value.Value<string>())))
                                property.Value = RedactedValue;
                        }
                        else
                        {
                            RedactToken(property.Value, names);
                        }
                    }
                    break;

                case JArray array:
                    foreach (var item in array)
                        RedactToken(item, names);
                    break;

                case JValue { Type: JTokenType.String } value:
                    var s = value.Value<string>();
                    if (!names.MentionedIn(s))
                        break;
                    var trimmed = s.TrimStart();
                    if ((trimmed.StartsWith('{') || trimmed.StartsWith('[')) && TryParseToken(s, out var nested))
                    {
                        RedactToken(nested, names);
                        value.Value = nested.ToString(Formatting.None);
                    }
                    else
                    {
                        value.Value = names.RedactWithPatterns(s);
                    }
                    break;
            }
        }

        private static bool TryParseToken(string json, out JToken token)
        {
            token = null;
            try
            {
                token = JsonConvert.DeserializeObject<JToken>(json, ParseSettings);
                return token != null && token.Type is JTokenType.Object or JTokenType.Array;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private sealed class NameSet
        {
            private readonly string[] _names;
            private readonly HashSet<string> _set;
            // "name": "value" (plain JSON text) and \"name\": \"value\" (JSON inside a serialized string). The
            // name must be the whole quoted property name, so "code" never matches "errorCode".
            private readonly Regex _plain;
            private readonly Regex _escaped;

            public NameSet(IEnumerable<string> names)
            {
                _names = names.ToArray();
                _set = new HashSet<string>(_names, StringComparer.OrdinalIgnoreCase);
                var alternation = string.Join('|', _names.Select(Regex.Escape));
                const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
                _plain = new Regex(@"(""(?:" + alternation + @")""\s*:\s*"")((?:[^""\\]|\\.)*)("")", options);
                _escaped = new Regex(@"(\\""(?:" + alternation + @")\\""\s*:\s*\\"")(.*?)(?<!\\)(\\"")", options);
            }

            public bool Contains(string name) => _set.Contains(name);

            public bool MentionedIn(string text)
            {
                foreach (var name in _names)
                {
                    if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                return false;
            }

            public string RedactWithPatterns(string text)
            {
                var result = _plain.Replace(text, Mask);
                return _escaped.Replace(result, Mask);
            }

            private static string Mask(Match m) =>
                m.Groups[2].Length == 0 ? m.Value : m.Groups[1].Value + RedactedValue + m.Groups[3].Value;
        }
    }
}
