using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.Common.AI.Application.Filters
{
    public class SemanticKernelInvocationLogger : IFunctionInvocationFilter
    {
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly IChatTurnErrorReporter _chatTurnErrorReporter;
        private readonly ILogger<SemanticKernelInvocationLogger> _logger;

        public SemanticKernelInvocationLogger(IChatNotificationService notificationService, ChatContext chatContext,
            IChatTurnErrorReporter chatTurnErrorReporter = null, ILogger<SemanticKernelInvocationLogger> logger = null)
        {
            _notificationService = notificationService;
            _chatContext = chatContext;
            _chatTurnErrorReporter = chatTurnErrorReporter;
            _logger = logger ?? NullLogger<SemanticKernelInvocationLogger>.Instance;
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            var fn = context.Function;

            // Start tool execution
            await _notificationService.SendToolExecutionUpdate(
                _chatContext.SessionId,
                $"Function Call Content",
                "started",
                $"{fn.PluginName} {fn.Name} \n Arguments: {string.Join(", ", context.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? new List<string>())}",
                200
            );

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.CancellationToken.IsCancellationRequested)
            {
                // US 7959 / Task 7961: a tool threw. The full exception stays in the logs; the chat gets a code only - the
                // "failed" update and, once per tool per turn, the non-terminal "tool not available" warning. Rethrown so
                // the run handles it as before (the model is told the call failed). A Stop (cancelled token) is no error.
                sw.Stop();
                _logger.LogError(ex, "Tool {PluginName} {FunctionName} failed after {ElapsedMs}ms in session {SessionId}",
                    fn.PluginName, fn.Name, sw.ElapsedMilliseconds, _chatContext.SessionId);

                // PR 16989 review: once per tool per turn, like the warning (same tool key); without a registered turn,
                // every failure still sends it.
                var toolKey = fn.PluginName ?? fn.Name;
                if (_chatTurnErrorReporter == null || _chatTurnErrorReporter.TryClaimToolFailure(toolKey))
                {
                    await _notificationService.SendToolExecutionFailedUpdate(
                        _chatContext.SessionId,
                        "Function Result Content",
                        ChatErrorCodes.ChatToolFailed,
                        $"{fn.PluginName} {fn.Name}");
                }

                if (_chatTurnErrorReporter != null)
                    await _chatTurnErrorReporter.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, toolKey);

                throw;
            }

            sw.Stop();

            // End tool execution
            await _notificationService.SendToolExecutionUpdate(
                _chatContext.SessionId,
                "Text Content",
                "completed",
                $"Duration: {sw.ElapsedMilliseconds}ms. \n Result: {JsonConvert.SerializeObject(context.Result.ToString())}",
                200
            );

            await _notificationService.SendToolExecutionUpdate(
                _chatContext.SessionId,
                "Function Result Content",
                "completed",
                $"{fn.PluginName} {fn.Name}",
                200
            );
        }
    }
}
