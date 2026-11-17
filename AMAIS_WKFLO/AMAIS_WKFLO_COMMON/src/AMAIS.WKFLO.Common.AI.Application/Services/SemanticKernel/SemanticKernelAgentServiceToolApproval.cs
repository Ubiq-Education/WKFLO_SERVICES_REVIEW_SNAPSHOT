using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel
{
    /// <summary>WI 7575: the "Ask for permission" parts of a text run (request, resume, card sync).</summary>
    public partial class SemanticKernelAgentService
    {
        /// <summary>
        /// Fail closed: approvals are only available when the run is interactive AND the approval services are
        /// registered. Otherwise nobody could ever answer an Ask call, so Ask must act as Block.
        /// </summary>
        internal bool ResolveApprovalsAvailable(bool interactiveRun) =>
            interactiveRun && _toolApprovalStateService != null && _toolApprovalExpiryScheduler != null;

        /// <summary>
        /// Bug 7722: in the turn resumed after a Deny (or expiry), the denied call is not offered again - otherwise the
        /// model just asks again. That MCP function, or the whole non-MCP tool, is Block for this turn only (not
        /// registered, refused by the enforcement filter); a later turn builds a fresh policy and asks normally.
        /// </summary>
        internal static ToolPermissionPolicy PolicyForResumedTurn(ToolPermissionPolicy policy, ToolApprovalRecord resume) =>
            resume == null || resume.Status == ToolApprovalConsts.Status.Approved
                ? policy
                : policy.WithBlockedForThisRun(resume.ToolId, resume.FunctionName);

        /// <summary>Registers the approval filter when someone can answer approval requests in this run.</summary>
        private async Task<ToolApprovalAutoFunctionInvocationFilter> AddToolApprovalFilterAsync(Kernel kernel, RunTextOptions options,
            ToolPermissionPolicy policy, Guid userMessageId, CancellationToken cancellationToken)
        {
            if (!policy.ApprovalsAvailable || _toolApprovalStateService == null || _toolApprovalExpiryScheduler == null)
                return null;

            var toolNames = (options.Agent.AgentTools ?? [])
                .Where(at => at.Tool != null)
                .GroupBy(at => at.Tool.Id)
                .ToDictionary(g => g.Key, g => g.First().Tool.Name);

            var runInfo = new ToolApprovalRunInfo(
                _tenantContext.TenantName,
                _userContext?.Guid,
                options.SessionId,
                options.Agent.Id,
                options.Agent.Name,
                _agentSharableContext?.ShareId,
                await TryGetChatbotIconUrlAsync(options.Agent.Id, options.RootIsWidget, cancellationToken),
                userMessageId,
                toolNames,
                ToolApprovalChatState.From(_chatContext, options.RootIsWidget),
                options.RootAgentId,
                // Bug 7722: the call this resumed turn just ran after "Allow once" (with "Allow for this conversation"
                // further calls are consented to and run through the grant as usual).
                options.ToolApprovalResume?.Decision == ToolApprovalConsts.Decision.AllowOnce ? options.ToolApprovalResume : null,
                // WI 7726: friendly display values of the MCP functions, from the tool copies this run executes.
                ToolApprovalRunInfo.IndexMcpFunctions((options.Agent.AgentTools ?? []).Select(at => at.Tool)));

            var filter = new ToolApprovalAutoFunctionInvocationFilter(policy, _toolApprovalStateService, _toolApprovalExpiryScheduler,
                _notificationService, _chatContext, _dateTimeProvider, runInfo, _logger);
            kernel.AutoFunctionInvocationFilters.Add(filter);
            return filter;
        }

        /// <summary>
        /// Brings every approval card still shown as Pending in the loaded history up to date with its stored record
        /// (decided elsewhere, or expired), and turns the placeholder result of an expired call into a "not run" result.
        /// </summary>
        internal async Task SyncToolApprovalCardsAsync(ChatHistory chatHistory, CancellationToken cancellationToken)
        {
            if (_toolApprovalStateService == null || chatHistory == null)
                return;

            foreach (var approvalId in ToolApprovalHistory.GetPendingCardApprovalIds(chatHistory))
            {
                try
                {
                    var record = await _toolApprovalStateService.GetAsync(approvalId, cancellationToken);
                    if (record == null)
                        continue;

                    if (record.IsPending && _dateTimeProvider.DateTimeOffsetUtcNow() >= record.ExpiresAt)
                    {
                        record = await _toolApprovalStateService.TryExpireAsync(approvalId, _dateTimeProvider.DateTimeOffsetUtcNow(), cancellationToken) ?? record;
                    }

                    if (record.IsPending)
                        continue;

                    ToolApprovalHistory.TryUpdateCard(chatHistory, record);
                    if (record.Decision == ToolApprovalConsts.Decision.Expired)
                    {
                        ToolApprovalHistory.TryReplaceFunctionResult(chatHistory, record.CallId, ToolApprovalHistory.ExpiredResult,
                            onlyIfCurrent: ToolApprovalHistory.AwaitingApprovalResult);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Syncing tool approval card {ApprovalId} failed", approvalId);
                }
            }
        }

        /// <summary>
        /// Resume after a decision: runs the STORED call when approved (no model replay) or records the refusal,
        /// puts the outcome in place of the call's placeholder result, updates the card and returns the ephemeral
        /// cue the resume turn sends to the model.
        /// </summary>
        internal async Task<string> ApplyToolApprovalDecisionAsync(Kernel kernel, ToolPermissionPolicy policy, ChatHistory chatHistory,
            ToolApprovalRecord record, CancellationToken cancellationToken)
        {
            var approved = record.Status == ToolApprovalConsts.Status.Approved;
            string result;

            if (!approved)
            {
                result = ToolApprovalHistory.ResultForNotRun(record.Decision);
            }
            else if (policy.GetPermission(record.PluginName, record.FunctionName) == ToolPermissionValues.Block
                || !kernel.Plugins.TryGetFunction(record.PluginName, record.FunctionName, out var function))
            {
                // Blocked, disabled or disconnected since the request was made.
                result = ToolApprovalHistory.UnavailableResult;
            }
            else
            {
                try
                {
                    // Bug 7722: the stored call runs before the turn itself starts, so the conversation must already be
                    // visible to plugins that read it (edit_image takes its source image from it). Without this it
                    // failed ("No image found"), and the model called it again - a new approval card after every Allow once.
                    _chatContext.CurrentChatHistory = chatHistory;

                    var invocation = await kernel.InvokeAsync(function, BuildArguments(record.ArgumentsJson, function), cancellationToken);
                    result = FormatApprovedResult(invocation);
                    _logger.LogInformation("Approved tool call {ApprovalId} ({PluginName}.{FunctionName}) ran in the resumed turn of agent {AgentId}",
                        record.ApprovalId, record.PluginName, record.FunctionName, record.AgentId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Running approved tool call {ApprovalId} failed", record.ApprovalId);
                    result = ToolApprovalHistory.FailedResult;
                }
            }

            var resultInHistory = ToolApprovalHistory.TryReplaceFunctionResult(chatHistory, record.CallId, result);
            ToolApprovalHistory.TryUpdateCard(chatHistory, record);

            var cue = approved
                ? $"[I approved the call to {record.FunctionName} from {record.ToolName}; it has run. Please continue with my previous request.]"
                : $"[I did not allow the call to {record.FunctionName} from {record.ToolName}. Please continue with my previous request without it.]";

            // Without short-term memory the placeholder is not in the history: hand the outcome over in the cue.
            return resultInHistory ? cue : $"{cue}\nTool result: {result}";
        }

        /// <summary>
        /// Rebuilds the stored call's arguments. Bug 7722: when <paramref name="function"/> declares a CLR type for a
        /// parameter (built-in plugins, e.g. <c>edit_image</c>), the value is converted to that type - a method-based
        /// function cannot take a raw <see cref="System.Text.Json.JsonElement"/> for a <c>string</c>/<c>int</c>/enum
        /// parameter. Parameters without a CLR type (MCP / AIFunction-based functions) get the JSON as before:
        /// strings as strings, everything else as a <see cref="System.Text.Json.JsonElement"/>. Arguments that were not
        /// stored are left out, so optional parameters keep their defaults.
        /// </summary>
        /// <summary>
        /// The approved call's result as the model should see it. <see cref="FunctionResult.ToString"/> returns the value's own
        /// ToString(), which for a non-string result (e.g. the AMAIS plugins' <c>List&lt;PluginResultItem&gt;</c>) is just
        /// its type name. Serialize it as JSON instead, as the normal tool-call path does.
        /// </summary>
        internal static string FormatApprovedResult(FunctionResult invocation)
        {
            var value = invocation?.GetValue<object>();
            if (value == null)
                return string.Empty;
            if (value is string text)
                return text;

            try
            {
                return System.Text.Json.JsonSerializer.Serialize(value, value.GetType());
            }
            catch (Exception ex) when (ex is NotSupportedException or System.Text.Json.JsonException)
            {
                // The tool already ran; an unserializable result must not turn it into a failure.
                return value.ToString();
            }
        }

        internal static KernelArguments BuildArguments(string argumentsJson, KernelFunction function = null)
        {
            var arguments = new KernelArguments();
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return arguments;

            System.Text.Json.JsonDocument document;
            try
            {
                document = System.Text.Json.JsonDocument.Parse(argumentsJson);
            }
            catch (System.Text.Json.JsonException)
            {
                return arguments;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return arguments;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var parameterType = function?.Metadata.Parameters
                        .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase))?.ParameterType;
                    arguments[property.Name] = ConvertArgument(property.Value, parameterType);
                }
            }

            return arguments;
        }

        private static object ConvertArgument(System.Text.Json.JsonElement value, Type parameterType)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Null)
                return null;

            if (parameterType == null)
                return value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.Clone();

            if (parameterType == typeof(string))
                return value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.GetRawText();

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(value.GetRawText(), parameterType, ArgumentJson.Options);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                // Leave it to the function's own conversion (and its error) rather than guessing.
                return value.Clone();
            }
        }

        /// <summary>
        /// After a turn that stopped for approval: answers any tool call left without a result and makes sure the turn
        /// ends with an assistant "needs approval" notice (tagged so it is never taken as a real answer).
        /// </summary>
        internal static string FinalizeToolApprovalTurn(ChatHistory chatHistory, ToolApprovalRecord approval, string assistantResponse,
            string messageId, DateTimeOffset now)
        {
            ToolApprovalHistory.CompleteDanglingFunctionCalls(chatHistory);

            var notice = string.IsNullOrWhiteSpace(assistantResponse)
                ? $"I need your permission to use {approval.FunctionName} from {approval.ToolName} before I can continue."
                : assistantResponse;

            var last = chatHistory.Count > 0 ? chatHistory[^1] : null;
            var metadata = new Dictionary<string, object>(last?.Role == AuthorRole.Assistant && last.Metadata != null ? last.Metadata : new Dictionary<string, object>())
            {
                [CommonMetadataConsts.Timestamp] = now,
                [CommonMetadataConsts.MessageId] = messageId,
                [CommonMetadataConsts.IsToolApprovalNotice] = true
            };

            if (last?.Role == AuthorRole.Assistant && !last.Items.OfType<FunctionCallContent>().Any())
                chatHistory.RemoveAt(chatHistory.Count - 1);

            chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, notice, metadata: metadata));
            return notice;
        }
    }
}
