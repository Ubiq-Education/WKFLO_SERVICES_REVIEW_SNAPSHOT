using AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Extensions
{
    /// <summary>
    /// US 7959 / Task 7961: an MCP tool that cannot be loaded for the turn (connect or list failure, broken config) is
    /// skipped with the generic, non-terminal "tool not available" warning; the full error stays in the logs. The OAuth
    /// reconnect flow keeps its own message and sends no warning.
    /// </summary>
    public class SemanticKernelExtensionsMcpToolWarningTests
    {
        private const int ToolId = 20;
        private const string PluginName = "plugin_20_MCP";
        private const string Sentinel = "SENTINEL-mcp-secret";

        private readonly ChatNotificationRecorder _chat = new();
        private readonly Mock<IMcpClientService> _mcp = new();
        private readonly Mock<IChatTurnErrorReporter> _reporter = new();

        private static TenantTool BuildMcpTool(string config = "{\"transportType\":\"Http\",\"endpoint\":\"https://mcp.example.test/mcp\"}") => new()
        {
            Id = ToolId,
            Name = "Example MCP",
            Type = "MCP",
            Config = config,
            IsActive = true
        };

        private Task<bool> InvokeAsync(TenantTool tool) =>
            Kernel.CreateBuilder().Build().AddWkflowToolsAsync(
                new TenantAgent { Id = 1, Name = "Main", AgentTools = [new AgentTool { AgentId = 1, ToolId = tool.Id, Tool = tool }] },
                Mock.Of<IDatastreamProvider>(),
                _chat.Object,
                Options.Create(new ElasticsearchSettings()),
                _mcp.Object,
                new TenantContext(),
                new ChatContext { SessionId = "session-1" },
                new UserContext(),
                agentTagNames: null,
                mapper: WkfloMapperExtensions.CreateMapper(),
                dateTimeProvider: Mock.Of<IDateTimeProvider>(),
                logger: NullLogger.Instance,
                chatTurnErrorReporter: _reporter.Object);

        private void VerifyWarning(Func<Times> times) =>
            _reporter.Verify(x => x.ReportWarningAsync(ChatErrorCodes.ChatToolUnavailable, PluginName, 1, "Main"), times);

        [Fact]
        public async Task ConnectReturnsFalse_ReportsTheToolUnavailableWarning()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await InvokeAsync(BuildMcpTool());

            VerifyWarning(Times.Once);
        }

        [Fact]
        public async Task ConnectThrows_ReportsTheWarning_WithoutTheExceptionText()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException(Sentinel));

            await InvokeAsync(BuildMcpTool());

            VerifyWarning(Times.Once);
            _chat.AssertNoLeak(Sentinel, nameof(HttpRequestException));
        }

        [Fact]
        public async Task ListToolsThrows_ReportsTheWarning()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mcp.Setup(x => x.ListToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(Sentinel));

            await InvokeAsync(BuildMcpTool());

            VerifyWarning(Times.Once);
            _chat.AssertNoLeak(Sentinel);
        }

        [Fact]
        public async Task BrokenConfig_SkipsOnlyThisTool_WithTheWarning()
        {
            var exception = await Record.ExceptionAsync(() => InvokeAsync(BuildMcpTool(config: "not json at all")));

            Assert.Null(exception);
            VerifyWarning(Times.Once);
        }

        [Fact]
        public async Task ConnectedWithNoAllowedFunctions_SendsNoWarning()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _mcp.Setup(x => x.ListToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<McpClientTool>());

            await InvokeAsync(BuildMcpTool());

            _reporter.Verify(x => x.ReportWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InteractiveConsentRequired_KeepsTheReconnectFlow_AndSendsNoWarning()
        {
            _mcp.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthInteractiveConsentRequiredException("needs interactive consent"));

            await InvokeAsync(BuildMcpTool());

            _reporter.Verify(x => x.ReportWarningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
            Assert.Contains(_chat.Sent, s => s.Method == AMAIS.WKFLO.Common.Models.CommonConsts.HubReceiverMethods.ReceiveMessage);
        }
    }
}
