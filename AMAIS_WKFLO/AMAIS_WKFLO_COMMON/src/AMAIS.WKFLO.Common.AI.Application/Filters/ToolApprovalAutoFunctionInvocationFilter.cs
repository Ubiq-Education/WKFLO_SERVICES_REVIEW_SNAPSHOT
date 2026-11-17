using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>Who/what the current turn is, captured once per run for the approval records it may create.</summary>
    public sealed record ToolApprovalRunInfo(
        string TenantName,
        string UserId,
        string SessionId,
        int AgentId,
        string AgentName,
        string ShareId,
        string ChatbotIconUrl,
        Guid? UserMessageId,
        IReadOnlyDictionary<int, string> ToolNames,
        ToolApprovalChatState Chat = null,
        int? RootAgentId = null,
        ToolApprovalRecord AlreadyRunCall = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, McpFunctionDescriptor>> McpFunctions = null)
    {
        /// <summary>
        /// WI 7726: the run's MCP function snapshot entry (from the same tool copy the run executes - the published
        /// tool for a shared chat), used for the approval card's friendly display values. Null when unknown.
        /// </summary>
        public McpFunctionDescriptor FindMcpFunction(int toolId, string functionName) =>
            functionName != null
            && McpFunctions != null
            && McpFunctions.TryGetValue(toolId, out var functions)
            && functions.TryGetValue(functionName, out var function)
                ? function
                : null;

        /// <summary>WI 7726: indexes the MCP function snapshots of the given tools by tool id and function name.</summary>
        public static IReadOnlyDictionary<int, IReadOnlyDictionary<string, McpFunctionDescriptor>> IndexMcpFunctions(IEnumerable<MD.Domain.Entities.Tool> tools) =>
            (tools ?? [])
                .Where(t => t != null && t.Type == Models.CommonConsts.CommonToolTypeConsts.MCP)
                .GroupBy(t => t.Id)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<string, McpFunctionDescriptor>)ToolPermissionJson.ParseDiscoveredFunctions(g.First().DiscoveredFunctions)
                        .GroupBy(f => f.Name, StringComparer.Ordinal)
                        .ToDictionary(f => f.Key, f => f.First(), StringComparer.Ordinal));
    }

    /// <summary>
    /// WI 7575: "Ask for permission". When the model calls a function whose effective permission is Ask (and no
    /// "Allow for this conversation" grant covers it), the call is NOT run: it is stored as a pending approval,
    /// the chatting person gets a <c>ToolApprovalRequest</c> SignalR event plus a persisted Forms-channel card,
    /// the call's result becomes a "pending approval" placeholder and the turn is terminated. The decision
    /// endpoints later run the STORED call (no model replay) and resume the turn.
    /// Only one approval is requested per turn; any further Ask call in the same turn is not run.
    /// Only registered when approvals are available - otherwise Ask functions were never registered (Block).
    /// </summary>
    public sealed class ToolApprovalAutoFunctionInvocationFilter : IAutoFunctionInvocationFilter
    {
        private readonly ToolPermissionPolicy _policy;
        private readonly IToolApprovalStateService _stateService;
        private readonly IToolApprovalExpiryScheduler _expiryScheduler;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ToolApprovalRunInfo _runInfo;
        private readonly ILogger _logger;

        public ToolApprovalAutoFunctionInvocationFilter(ToolPermissionPolicy policy, IToolApprovalStateService stateService,
            IToolApprovalExpiryScheduler expiryScheduler, IChatNotificationService notificationService, ChatContext chatContext,
            IDateTimeProvider dateTimeProvider, ToolApprovalRunInfo runInfo, ILogger logger)
        {
            _policy = policy;
            _stateService = stateService;
            _expiryScheduler = expiryScheduler;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _dateTimeProvider = dateTimeProvider;
            _runInfo = runInfo;
            _logger = logger;
        }

        /// <summary>The approval requested during this turn, if any.</summary>
        public ToolApprovalRecord RequestedApproval { get; private set; }

        public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
        {
            var pluginName = context.Function?.PluginName;
            var functionName = context.Function?.Name;

            // Bug 7722 loop guard: in the turn resumed after "Allow once", the approved call has already run. The same
            // call again (same function, same arguments) is neither asked for again nor run twice; the model is told to
            // use the result it already has. A call with other arguments is a new request and is handled normally.
            // After "Allow for this conversation" the person consented to further calls: a repeat runs via the grant.
            if (IsAlreadyRunCall(_runInfo.AlreadyRunCall, pluginName, functionName, TryGetArguments(context)))
            {
                _logger.LogInformation("Tool approval {ApprovalId}: {PluginName}.{FunctionName} repeated with the same arguments in the resumed turn; not asking again",
                    _runInfo.AlreadyRunCall.ApprovalId, pluginName, functionName);
                context.Result = new FunctionResult(context.Function, ToolApprovalHistory.AlreadyDoneResult);
                return;
            }

            if (_policy.GetPermission(pluginName, functionName) != ToolPermissionValues.Ask
                || !ToolPermissionPolicy.TryParseToolId(pluginName, out var toolId))
            {
                await next(context);
                return;
            }

            var isMcp = _policy.IsMcpTool(toolId);
            var grantKey = ToolApprovalGrantKey.For(toolId, isMcp ? functionName : null);
            if (await _stateService.HasConversationGrantAsync(_runInfo.SessionId, grantKey, context.CancellationToken))
            {
                await next(context);
                return;
            }

            if (RequestedApproval != null)
            {
                Stop(context, ToolApprovalHistory.NotRunResult);
                return;
            }

            var now = _dateTimeProvider.DateTimeOffsetUtcNow();
            var mcpFunction = isMcp ? _runInfo.FindMcpFunction(toolId, functionName) : null;
            var record = new ToolApprovalRecord
            {
                ApprovalId = Guid.NewGuid().ToString(),
                TenantName = _runInfo.TenantName,
                UserId = _runInfo.UserId,
                SessionId = _runInfo.SessionId,
                AgentId = _runInfo.AgentId,
                AgentName = _runInfo.AgentName,
                ShareId = _runInfo.ShareId,
                ChatbotIconUrl = _runInfo.ChatbotIconUrl,
                ToolId = toolId,
                ToolName = _runInfo.ToolNames != null && _runInfo.ToolNames.TryGetValue(toolId, out var toolName) ? toolName : pluginName,
                IsMcp = isMcp,
                PluginName = pluginName,
                FunctionName = functionName,
                FunctionDescription = context.Function?.Description,
                FunctionDisplayName = McpFunctionDescriptor.NormalizeDisplayValue(mcpFunction?.DisplayName),
                FunctionDisplayDescription = McpFunctionDescriptor.NormalizeDisplayValue(mcpFunction?.DisplayDescription),
                ArgumentsJson = SerializeArguments(TryGetArguments(context)),
                CallId = context.ToolCallId,
                UserMessageId = _runInfo.UserMessageId,
                Chat = _runInfo.Chat ?? new ToolApprovalChatState(),
                RootAgentId = _runInfo.RootAgentId,
                CardMessageId = Guid.NewGuid().ToString(),
                Status = ToolApprovalConsts.Status.Pending,
                CreatedAt = now,
                ExpiresAt = now.Add(ToolApprovalConsts.Expiry)
            };

            await _stateService.SaveNewAsync(record, context.CancellationToken);
            RequestedApproval = record;

            // Bug 7722: a hand-off sub-agent's activity shows "awaiting approval" now, before the card is shown, so a
            // decision (and its "resumed"/"denied" update) can only come after it. Root-agent approvals have no activity.
            if (record.RootAgentId != null)
                await ToolApprovalNotifier.SendSubagentActivityAsync(_notificationService, record, Models.SignalR.SubagentActivityStatus.AwaitingApproval, logger: _logger);
            _chatContext.ToolApprovalRequestedThisTurn = true;

            try
            {
                await _expiryScheduler.ScheduleAsync(new ToolApprovalExpiryWorkflowInput { ApprovalId = record.ApprovalId });
            }
            catch (Exception ex)
            {
                // Not fatal: a late decision is still rejected as expired (ExpiresAt is checked on decide).
                _logger.LogWarning(ex, "Scheduling the expiry of tool approval {ApprovalId} failed", record.ApprovalId);
            }

            // Persisted (and pushed as a normal chat message) after this turn's reply, so a history reload
            // re-renders the card; the dedicated event lets the chat UI show it immediately.
            _chatContext.AssistantPostMessagesToBeProcessed.Add(ToolApprovalHistory.CreateCardMessage(record, now));
            await _notificationService.SendToolApprovalRequestAsync(record.SessionId, ToolApprovalHistory.BuildRequestUpdate(record));

            Stop(context, ToolApprovalHistory.AwaitingApprovalResult);
        }

        /// <summary>Bug 7722: whether this call is the approved call of the resumed turn again (same function and arguments).</summary>
        internal static bool IsAlreadyRunCall(ToolApprovalRecord alreadyRun, string pluginName, string functionName, KernelArguments arguments)
        {
            if (alreadyRun == null
                || alreadyRun.Decision != ToolApprovalConsts.Decision.AllowOnce
                || !string.Equals(alreadyRun.PluginName, pluginName, StringComparison.Ordinal)
                || !string.Equals(alreadyRun.FunctionName, functionName, StringComparison.Ordinal))
                return false;

            try
            {
                return System.Text.Json.Nodes.JsonNode.DeepEquals(ParseArguments(alreadyRun.ArgumentsJson), ParseArguments(SerializeArguments(arguments)));
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        // Absent and null arguments are the same as not passing them.
        private static System.Text.Json.Nodes.JsonObject ParseArguments(string json)
        {
            var parsed = string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            var result = new System.Text.Json.Nodes.JsonObject();
            foreach (var (name, value) in parsed ?? [])
            {
                if (value != null)
                    result[name] = value.DeepClone();
            }
            return result;
        }

        private static void Stop(AutoFunctionInvocationContext context, string result)
        {
            context.Result = new FunctionResult(context.Function, result,
                metadata: new Dictionary<string, object> { [GuardrailResultMetadata.IsGuardrailKey] = true });
            context.Terminate = true;
        }

        // AutoFunctionInvocationContext.Arguments throws when the context was created without KernelArguments
        // (it can happen on the IChatClient-based function invocation path); a missing argument list must never
        // break the approval request, it just shows as "{}".
        private static KernelArguments TryGetArguments(AutoFunctionInvocationContext context)
        {
            try
            {
                return context.Arguments;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Stores the model's arguments as a JSON object. Bug 7722: written with System.Text.Json - the values the
        /// connectors hand over are usually <see cref="System.Text.Json.JsonElement"/>s, which Newtonsoft serialized
        /// as their CLR shape (<c>{"ValueKind":3}</c>), losing the actual value.
        /// </summary>
        internal static string SerializeArguments(KernelArguments arguments)
        {
            var json = new System.Text.Json.Nodes.JsonObject();
            foreach (var (name, value) in arguments ?? [])
            {
                json[name] = ToJsonNode(value);
            }

            return json.ToJsonString();
        }

        private static System.Text.Json.Nodes.JsonNode ToJsonNode(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case System.Text.Json.JsonElement element:
                    return element.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null
                        ? null
                        : System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText());
                case System.Text.Json.Nodes.JsonNode node:
                    return node.DeepClone();
                case string text:
                    return System.Text.Json.Nodes.JsonValue.Create(text);
            }

            try
            {
                return System.Text.Json.JsonSerializer.SerializeToNode(value, ArgumentJson.Options);
            }
            catch (Exception ex) when (ex is NotSupportedException or System.Text.Json.JsonException or InvalidOperationException)
            {
                return System.Text.Json.Nodes.JsonValue.Create(value.ToString());
            }
        }
    }

    /// <summary>WI 7575 / Bug 7722: JSON options for stored tool-call arguments (camel/any case, enums as names).</summary>
    internal static class ArgumentJson
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }
}
