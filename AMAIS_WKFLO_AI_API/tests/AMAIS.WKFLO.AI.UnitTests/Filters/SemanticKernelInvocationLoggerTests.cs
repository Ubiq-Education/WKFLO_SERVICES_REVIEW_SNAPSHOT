using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Filters
{
    /// <summary>
    /// US 7959 / Task 7961: a tool that throws sends a "failed" tool update carrying only a code, reports the non-terminal
    /// "tool not available" warning, and is still rethrown to the run; a Stop is not a tool failure.
    /// </summary>
    public class SemanticKernelInvocationLoggerTests
    {
        private const string Sentinel = "SENTINEL-tool-secret";
        private const string PluginName = "plugin_7_HTTP";

        private readonly ChatNotificationRecorder _chat = new();
        private readonly Mock<IChatTurnErrorReporter> _reporter = new();

        public SemanticKernelInvocationLoggerTests()
        {
            _reporter.Setup(x => x.TryClaimToolFailure(It.IsAny<string>())).Returns(true);
        }

        private Kernel BuildKernel(Func<CancellationToken, string> body, out KernelFunction function)
        {
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions(PluginName, [KernelFunctionFactory.CreateFromMethod(body, "call_api")]);
            function = kernel.Plugins[PluginName]["call_api"];
            kernel.FunctionInvocationFilters.Add(new SemanticKernelInvocationLogger(_chat.Object, new ChatContext { SessionId = "session-1" }, _reporter.Object));
            return kernel;
        }

        private ToolExecutionUpdate FailedUpdate() => _chat.Sent
            .Where(s => s.Method == HubReceiverMethods.ToolExecutionUpdate)
            .Select(s => (ToolExecutionUpdate)s.Payload)
            .SingleOrDefault(u => u.Status == "failed");

        [Fact]
        public async Task ToolThrows_SendsAFailedUpdateWithACodeOnly_ReportsTheWarning_AndRethrows()
        {
            var kernel = BuildKernel(_ => throw new InvalidOperationException(Sentinel), out var function);

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken));

            var failed = FailedUpdate();
            Assert.NotNull(failed);
            Assert.Equal(ChatErrorCodes.ChatToolFailed, failed.ErrorCode);
            Assert.Null(failed.Error);
            Assert.Equal("session-1", failed.ChatId);
            _reporter.Verify(x => x.ReportWarningAsync(ChatErrorCodes.ChatToolFailed, PluginName, null, null), Times.Once);
            Assert.DoesNotContain(_chat.Sent, s => s.Method == HubReceiverMethods.ToolExecutionUpdate && ((ToolExecutionUpdate)s.Payload).Status == "completed");
            _chat.AssertNoLeak(Sentinel, nameof(InvalidOperationException));
        }

        [Fact]
        public async Task ToolSucceeds_SendsNoFailure()
        {
            var kernel = BuildKernel(_ => "ok", out var function);

            await kernel.InvokeAsync(function, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Null(FailedUpdate());
            _reporter.Verify(x => x.ReportWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StoppedDuringTheTool_IsNotAToolFailure()
        {
            using var stop = new CancellationTokenSource();
            var kernel = BuildKernel(token =>
            {
                stop.Cancel();
                throw new OperationCanceledException(token);
            }, out var function);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kernel.InvokeAsync(function, cancellationToken: stop.Token));

            Assert.Null(FailedUpdate());
            _reporter.Verify(x => x.ReportWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ToolThrows_WithoutAReporter_StillSendsTheFailedUpdate()
        {
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions(PluginName, [KernelFunctionFactory.CreateFromMethod(new Func<string>(() => throw new InvalidOperationException(Sentinel)), "call_api")]);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelInvocationLogger(_chat.Object, new ChatContext { SessionId = "session-1" }));

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ChatErrorCodes.ChatToolFailed, FailedUpdate()?.ErrorCode);
        }
        // --- PR 16989 review: the "failed" tool update is sent once per tool per turn ---------------------------------

        private List<ToolExecutionUpdate> FailedUpdates() => _chat.Sent
            .Where(s => s.Method == HubReceiverMethods.ToolExecutionUpdate)
            .Select(s => (ToolExecutionUpdate)s.Payload)
            .Where(u => u.Status == "failed")
            .ToList();

        /// <summary>A real reporter with a registered turn, as BaseWorkflowActivity sets it up.</summary>
        private static ChatTurnErrorReporter ReporterInATurn()
        {
            var notifier = new Mock<IChatTurnErrorNotifier>();
            notifier.Setup(x => x.NotifyAsync(It.IsAny<ChatTurnErrorNotification>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var reporter = new ChatTurnErrorReporter(notifier.Object, new ChatTurnFailureState(), NullLogger<ChatTurnErrorReporter>.Instance);
            reporter.BeginTurn(new ChatTurnErrorTurn { TenantName = "tenant-a", SessionId = "session-1", TurnId = "wf-1", IsStopRequested = () => false });
            return reporter;
        }

        private Kernel KernelWithTwoFailingTools(IChatTurnErrorReporter reporter)
        {
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromFunctions(PluginName, [KernelFunctionFactory.CreateFromMethod(new Func<string>(() => throw new InvalidOperationException(Sentinel)), "call_api")]);
            kernel.Plugins.AddFromFunctions("plugin_8_MCP", [KernelFunctionFactory.CreateFromMethod(new Func<string>(() => throw new InvalidOperationException(Sentinel)), "search")]);
            kernel.FunctionInvocationFilters.Add(new SemanticKernelInvocationLogger(_chat.Object, new ChatContext { SessionId = "session-1" }, reporter));
            return kernel;
        }

        [Fact]
        public async Task SameToolFailsTwiceInATurn_SendsOneFailedUpdate()
        {
            var kernel = KernelWithTwoFailingTools(ReporterInATurn());

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));

            var failed = Assert.Single(FailedUpdates());
            Assert.Equal(ChatErrorCodes.ChatToolFailed, failed.ErrorCode);
        }

        [Fact]
        public async Task AnotherToolFailingInTheSameTurn_StillSendsItsFailedUpdate()
        {
            var kernel = KernelWithTwoFailingTools(ReporterInATurn());

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins["plugin_8_MCP"]["search"], cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(2, FailedUpdates().Count);
            Assert.Contains(FailedUpdates(), u => u.Description.StartsWith("plugin_8_MCP", StringComparison.Ordinal));
        }

        [Fact]
        public async Task NoReporter_EveryFailureStillSendsItsFailedUpdate()
        {
            var kernel = KernelWithTwoFailingTools(reporter: null);

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(2, FailedUpdates().Count);
        }

        [Fact]
        public async Task ReporterWithoutARegisteredTurn_EveryFailureStillSendsItsFailedUpdate()
        {
            var reporter = new ChatTurnErrorReporter(Mock.Of<IChatTurnErrorNotifier>(), new ChatTurnFailureState(), NullLogger<ChatTurnErrorReporter>.Instance);
            var kernel = KernelWithTwoFailingTools(reporter);

            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<Exception>(() => kernel.InvokeAsync(kernel.Plugins[PluginName]["call_api"], cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(2, FailedUpdates().Count);
        }
    }
}
