using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using Microsoft.SemanticKernel;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    /// <summary>
    /// US 7501: before each tool call the model asks for, reads the session's stop state once (see
    /// <see cref="IChatStopCheckpoint"/>). A stopped turn then ends instead of running another tool.
    /// </summary>
    public sealed class ChatStopCheckpointFilter : IAutoFunctionInvocationFilter
    {
        private readonly IChatStopCheckpoint _checkpoint;
        private readonly string _tenantName;
        private readonly string _sessionId;

        public ChatStopCheckpointFilter(IChatStopCheckpoint checkpoint, string tenantName, string sessionId)
        {
            _checkpoint = checkpoint;
            _tenantName = tenantName;
            _sessionId = sessionId;
        }

        public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
        {
            await _checkpoint.ThrowIfStoppedAsync(_tenantName, _sessionId, ChatStopCheckpoints.ToolCall, context.CancellationToken);
            await next(context);
        }
    }
}
