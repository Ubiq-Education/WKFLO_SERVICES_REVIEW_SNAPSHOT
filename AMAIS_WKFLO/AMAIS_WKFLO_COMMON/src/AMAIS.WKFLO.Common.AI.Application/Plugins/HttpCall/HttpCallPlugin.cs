using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.HttpCall
{
    public class HttpCallPlugin
    {
        private readonly Tool _tool;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly ILogger _logger;

        public HttpCallPlugin(Tool tool, IChatNotificationService notificationService, ChatContext chatContext, ILogger logger)
        {
            _tool = tool;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _logger = logger;
        }

        // ── Plugin factory ───────────────────────────────────────────────────

        /// <summary>
        /// Builds the <see cref="KernelPlugin"/> for this tool.
        /// When the tool config defines inputs, a function with one named parameter per input
        /// is generated so the LLM receives accurate type/description metadata.
        /// Otherwise the simple attribute-based function is used.
        /// </summary>
        public static KernelPlugin BuildKernelPlugin(
            Tool tool,
            IChatNotificationService notificationService,
            ChatContext chatContext,
            string pluginName,
            ILogger logger)
        {
            var instance = new HttpCallPlugin(tool, notificationService, chatContext, logger);

            HttpCallToolConfig config = null;
            if (!string.IsNullOrWhiteSpace(tool.Config))
                config = JsonConvert.DeserializeObject<HttpCallToolConfig>(tool.Config);

            var inputs = config?.Inputs ?? [];

            if (inputs.Count == 0)
                return KernelPluginFactory.CreateFromObject(instance, pluginName);

            // Build one KernelParameterMetadata per declared input
            var parameters = inputs.Select(kv =>
            {
                var def = kv.Value;
                var desc = def.Description ?? def.Name ?? kv.Key;
                if (def.HasDefaultValue)
                    desc += $" (default: {def.DefaultValue})";

                return new KernelParameterMetadata(kv.Key)
                {
                    Description = desc,
                    IsRequired = def.Required && !def.HasDefaultValue,
                    DefaultValue = def.HasDefaultValue ? def.DefaultValue : null,
                    ParameterType = MapType(def.Type)
                };
            }).ToList();

            var funcDesc = $"Executes HTTP {config?.Method ?? "GET"} request. Tool: {tool.Name}. {tool.Description}";

            // KernelArguments is a special SK type — when used as a delegate parameter,
            // SK injects all LLM-provided arguments into it keyed by parameter name.
            var function = KernelFunctionFactory.CreateFromMethod(
                method: (KernelArguments args, CancellationToken ct) =>
                    instance.ExecuteWithArgsAsync(args, config, inputs, ct),
                functionName: "execute_http_call",
                description: funcDesc,
                parameters: parameters
            );

            return KernelPluginFactory.CreateFromFunctions(pluginName, [function]);
        }

        // ── No-inputs path (attribute-based, used when inputs = {}) ─────────

        [KernelFunction("execute_http_call")]
        [Description("Executes a pre-configured HTTP request and returns the response body as text. Use this to retrieve data from external APIs.")]
        public async Task<string> ExecuteHttpCallAsync(
            [Description("Optional JSON object with key-value pairs to merge into the request parameters or body, overriding static config values where keys match.")] string dynamicInputs = null)
        {
            if (string.IsNullOrWhiteSpace(_tool.Config))
                return "HTTP tool configuration is missing or invalid.";

            var config = JsonConvert.DeserializeObject<HttpCallToolConfig>(_tool.Config);

            if (config == null || string.IsNullOrWhiteSpace(config.Url))
                return "HTTP tool configuration is missing or invalid.";

            // Legacy: merge free-form JSON overrides directly into the parameters dict
            if (!string.IsNullOrWhiteSpace(dynamicInputs))
            {
                var extra = JsonConvert.DeserializeObject<Dictionary<string, string>>(dynamicInputs);
                if (extra != null)
                {
                    var merged = new Dictionary<string, string>(config.Parameters ?? []);
                    foreach (var kv in extra)
                        merged[kv.Key] = kv.Value;
                    config.Parameters = merged;
                }
            }

            // Pass empty resolved inputs — no {{placeholder}} substitution in the legacy path
            return await ExecuteCoreAsync(config, new Dictionary<string, string>());
        }

        // ── Inputs path (called via dynamically-built KernelFunction) ────────

        internal async Task<string> ExecuteWithArgsAsync(
            KernelArguments args,
            HttpCallToolConfig config,
            Dictionary<string, HttpCallInputDefinition> inputs,
            CancellationToken cancellationToken = default)
        {
            // Resolve each input value: LLM-provided > configured value > default
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();

            foreach (var (key, def) in inputs)
            {
                if (args.TryGetValue(key, out var argVal) && argVal != null)
                {
                    resolved[key] = argVal.ToString();
                }
                else if (!string.IsNullOrEmpty(def.Value))
                {
                    resolved[key] = def.Value;
                }
                else if (def.HasDefaultValue)
                {
                    resolved[key] = def.DefaultValue ?? string.Empty;
                }
                else if (def.Required)
                {
                    missing.Add(key);
                }
            }

            if (missing.Count > 0)
                return $"Cannot execute HTTP request: required input(s) not provided: {string.Join(", ", missing)}.";

            return await ExecuteCoreAsync(config, resolved);
        }

        // ── Core HTTP execution ──────────────────────────────────────────────

        private async Task<string> ExecuteCoreAsync(
            HttpCallToolConfig config,
            IReadOnlyDictionary<string, string> resolvedInputs)
        {
            try
            {
                // Substitute {{key}} placeholders using resolved input values
                var body = ResolvePlaceholders(config.Body, resolvedInputs);
                var parameters = ResolvePlaceholders(config.Parameters, resolvedInputs);
                var urlParams = ResolvePlaceholders(config.UrlParams, resolvedInputs);

                var request = config.Url.WithHeaders(config.Headers ?? []);

                if (urlParams?.Count > 0)
                    request = request.SetQueryParams(urlParams);

                IFlurlResponse response;
                var method = (config.Method ?? "GET").ToUpperInvariant();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    response = method switch
                    {
                        "POST" => await request.PostStringAsync(body),
                        "PUT" => await request.PutStringAsync(body),
                        "PATCH" => await request.PatchStringAsync(body),
                        _ => await request.SendAsync(new HttpMethod(method), new StringContent(body))
                    };
                }
                else if (parameters?.Count > 0)
                {
                    response = method switch
                    {
                        "POST" => await request.PostJsonAsync(parameters),
                        "PUT" => await request.PutJsonAsync(parameters),
                        "PATCH" => await request.PatchJsonAsync(parameters),
                        _ => await request.SendAsync(new HttpMethod(method))
                    };
                }
                else
                {
                    response = method switch
                    {
                        "DELETE" => await request.DeleteAsync(),
                        _ => await request.GetAsync()
                    };
                }

                var responseBody = await response.GetStringAsync();
                return TruncateResponse(responseBody, config.MaxResponseChars);
            }
            catch (FlurlHttpException ex)
            {
                var errorBody = await ex.GetResponseStringAsync();
                _logger.LogError(ex, "HttpCallPlugin HTTP error {StatusCode} for tool {ToolName}", ex.StatusCode, _tool.Name);

                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    $"HTTP request failed with status {ex.StatusCode}.",
                    statusCode: ex.StatusCode ?? 500
                );

                return $"HTTP request failed (status {ex.StatusCode}): {errorBody}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing HttpCallPlugin for tool {ToolName}", _tool.Name);
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    "HTTP request could not be completed.",
                    statusCode: 500
                );

                return "HTTP request could not be completed.";
            }
        }

        // ── Placeholder resolution ───────────────────────────────────────────

        private static string ResolvePlaceholders(string template, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template) || values == null || values.Count == 0)
                return template;

            foreach (var (key, value) in values)
                template = template.Replace($"{{{{{key}}}}}", value ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);

            return template;
        }

        private static Dictionary<string, string> ResolvePlaceholders(
            Dictionary<string, string> dict,
            IReadOnlyDictionary<string, string> values)
        {
            if (dict == null || dict.Count == 0 || values == null || values.Count == 0)
                return dict ?? [];

            return dict.ToDictionary(
                kv => kv.Key,
                kv => ResolvePlaceholders(kv.Value, values));
        }

        // ── Type mapping ─────────────────────────────────────────────────────

        private static Type MapType(string typeName) => typeName?.ToLowerInvariant() switch
        {
            "number" => typeof(double),
            "boolean" => typeof(bool),
            _ => typeof(string)   // string | array | object all arrive as strings
        };

        // ── Response cleaning & truncation ───────────────────────────────────

        private const int DefaultMaxResponseChars = 100_000;

        private static readonly HashSet<string> NoiseKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "task_id", "request_id", "crawl_time", "cost",
            "se_results_count", "api_version", "trace_id", "x-request-id"
        };

        private const int Base64MinLength = 256;

        private static readonly Regex WhitespaceRun =
            new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex Base64Pattern =
            new(@"^[A-Za-z0-9+/]{" + Base64MinLength + @",}={0,2}$", RegexOptions.Compiled);

        internal static string CleanResponse(string body)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            try
            {
                var token = JToken.Parse(body);
                PruneToken(token);
                // TokenToText already produces clean, structured text — return as-is
                return TokenToText(token).TrimEnd();
            }
            catch (JsonException)
            {
                // Not JSON — collapse whitespace runs into single spaces
                return WhitespaceRun.Replace(body.Trim(), " ");
            }
        }

        private static string TruncateResponse(string body, int? configuredMax)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            body = CleanResponse(body);

            var limit = configuredMax > 0 ? configuredMax.Value : DefaultMaxResponseChars;

            if (body.Length <= limit)
                return body;

            return body[..limit] +
                   $"\n\n[RESPONSE TRUNCATED — {body.Length.ToString("N0", CultureInfo.InvariantCulture)} characters returned, limit is {limit.ToString("N0", CultureInfo.InvariantCulture)}. " +
                   "Configure 'maxResponseChars' on the tool to adjust.]";
        }

        /// <summary>
        /// Converts a pruned <see cref="JToken"/> into a flat key-path: value text format.
        /// No structural JSON characters (<c>{ } [ ] " , :</c>) appear in the output,
        /// which reduces token consumption while remaining fully readable by the LLM.
        /// Example: <c>tasks[0].result.score: 92</c>
        /// </summary>
        private static string TokenToText(JToken token)
        {
            var sb = new StringBuilder();
            FlattenToken(token, string.Empty, sb, depth: 0);
            return sb.ToString();
        }

        private static void FlattenToken(JToken token, string prefix, StringBuilder sb, int depth)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                    if (prop.Value is JObject)
                    {
                        // Nested object → section header whose dash count reflects nesting depth:
                        //   - audits           (depth 0 → 1 dash)
                        //   -- largest-contentful-paint
                        //   score: 0.85
                        sb.Append($"{new string('-', depth + 1)} {key}\n");
                        FlattenToken(prop.Value, string.Empty, sb, depth + 1);
                    }
                    else
                    {
                        // Arrays and scalars keep the key as prefix so array labels stay readable
                        FlattenToken(prop.Value, key, sb, depth);
                    }
                }
            }
            else if (token is JArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    var label = $"{prefix}[{i}]";

                    if (arr[i] is JObject or JArray)
                    {
                        // Array item → section header at current depth, then reset prefix
                        sb.Append($"{new string('-', depth + 1)} {label}\n");
                        FlattenToken(arr[i], string.Empty, sb, depth + 1);
                    }
                    else
                    {
                        FlattenToken(arr[i], label, sb, depth);
                    }
                }
            }
            else
            {
                // Scalar value — booleans lowercased for readability
                var value = token.Type == JTokenType.Boolean
                    ? token.Value<bool>().ToString().ToLowerInvariant()
                    : token.ToString();

                sb.Append(string.IsNullOrEmpty(prefix) ? $"{value}\n" : $"{prefix}: {value}\n");
            }
        }

        private static void PruneToken(JToken token)
        {
            if (token is JObject obj)
            {
                var toRemove = new List<string>();

                foreach (var prop in obj.Properties())
                {
                    if (NoiseKeys.Contains(prop.Name)) { toRemove.Add(prop.Name); continue; }
                    if (IsEmpty(prop.Value)) { toRemove.Add(prop.Name); continue; }

                    if (prop.Value.Type == JTokenType.String)
                    {
                        var str = prop.Value.Value<string>();
                        if (IsBase64Blob(str)) { toRemove.Add(prop.Name); continue; }
                    }

                    PruneToken(prop.Value);

                    if (IsEmpty(prop.Value)) toRemove.Add(prop.Name);
                }

                foreach (var key in toRemove)
                    obj.Remove(key);
            }
            else if (token is JArray arr)
            {
                foreach (var child in arr)
                    PruneToken(child);
            }
        }

        private static bool IsEmpty(JToken token) => token.Type switch
        {
            JTokenType.Null => true,
            JTokenType.Undefined => true,
            JTokenType.String => string.IsNullOrWhiteSpace(token.Value<string>()),
            JTokenType.Array => !token.HasValues,
            JTokenType.Object => !token.HasValues,
            _ => false
        };

        private static bool IsBase64Blob(string value) =>
            value != null && value.Length >= Base64MinLength && Base64Pattern.IsMatch(value);
    }
}
