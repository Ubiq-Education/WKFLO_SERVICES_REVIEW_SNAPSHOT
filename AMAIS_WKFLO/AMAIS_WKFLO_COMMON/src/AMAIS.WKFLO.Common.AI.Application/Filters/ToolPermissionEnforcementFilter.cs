using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>
    /// WI 7447/7575 defense in depth: blocked tools and MCP functions that are not on the allowlist are never
    /// registered with the kernel in the first place (see <c>SemanticKernelExtensions.AddWkflowToolsAsync</c>),
    /// but anything that still reaches invocation (a lazily added plugin, a hallucinated call matching a stale
    /// registration, a future code path) is refused here without running. MCP has no "hide tools" call, and
    /// only this code ever sends <c>tools/call</c>, so this is the last gate. "Ask" is handled by the
    /// auto-invocation approval filter, not here.
    /// </summary>
    public sealed class ToolPermissionEnforcementFilter : IFunctionInvocationFilter
    {
        internal const string BlockedMessage =
            "[GUARDRAIL] This tool is not available to you. Do not call it again; continue without it and, if it matters, tell the user you cannot use it.";

        private readonly ToolPermissionPolicy _policy;

        public ToolPermissionEnforcementFilter(ToolPermissionPolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (_policy.GetPermission(context.Function?.PluginName, context.Function?.Name) == ToolPermissionValues.Block)
            {
                context.Result = new FunctionResult(context.Function, BlockedMessage,
                    metadata: new Dictionary<string, object> { [GuardrailResultMetadata.IsGuardrailKey] = true });
                return;
            }

            await next(context);
        }
    }
}
