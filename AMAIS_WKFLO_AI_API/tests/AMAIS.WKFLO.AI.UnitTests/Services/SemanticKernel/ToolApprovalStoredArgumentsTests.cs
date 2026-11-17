using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolPermissions;
using AMAIS.WKFLO.Common.AI.Application.Filters;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIFunction = Microsoft.Extensions.AI.AIFunction;
using AIFunctionArguments = Microsoft.Extensions.AI.AIFunctionArguments;

namespace AMAIS.WKFLO.AI.UnitTests.Services.SemanticKernel
{
    /// <summary>
    /// Bug 7722: an approved call is re-run from its STORED arguments. The model's arguments arrive as JsonElements;
    /// they must be stored without losing their values and converted back to each parameter's CLR type for built-in
    /// (method-based) functions like edit_image, while MCP/AIFunction-based functions keep receiving JSON values.
    /// The re-run is the same for a root agent and for a hand-off sub-agent.
    /// </summary>
    public class ToolApprovalStoredArgumentsTests
    {
        public enum Quality { Low, Medium, High }

        /// <summary>A built-in style plugin with typed parameters, like ImageGenerationPlugin.edit_image.</summary>
        public sealed class EditPlugin
        {
            public string Seen { get; private set; }

            [KernelFunction("edit_image"), Description("Edits an image")]
            public string EditImage(string instruction, int count, bool keepRatio, Quality quality, string fileNameJson = null)
            {
                Seen = $"{instruction}|{count}|{keepRatio}|{quality}|{fileNameJson ?? "<default>"}";
                return "edited";
            }
        }

        /// <summary>An MCP-style function: no CLR method, receives JSON-ish arguments.</summary>
        private sealed class RecordingAIFunction : AIFunction
        {
            public AIFunctionArguments Received { get; private set; }
            public override string Name => "search";
            public override string Description => "Searches documents";
            public override JsonElement JsonSchema { get; } = JsonDocument.Parse(
                "{\"type\":\"object\",\"properties\":{\"q\":{\"type\":\"string\"},\"limit\":{\"type\":\"integer\"},\"filters\":{\"type\":\"object\"}}}").RootElement;

            protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            {
                Received = arguments;
                return ValueTask.FromResult<object>("found");
            }
        }

        /// <summary>The arguments as the connectors hand them over: JsonElement values.</summary>
        private static KernelArguments ModelArguments(string json)
        {
            var arguments = new KernelArguments();
            foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject())
                arguments[property.Name] = property.Value.Clone();
            return arguments;
        }

        [Fact]
        public void SerializeArguments_KeepsTheValuesOfJsonElements()
        {
            var json = ToolApprovalAutoFunctionInvocationFilter.SerializeArguments(
                ModelArguments("{\"instruction\":\"make it blue\",\"count\":2,\"keepRatio\":true,\"filters\":{\"a\":1}}"));

            using var document = JsonDocument.Parse(json);
            Assert.Equal("make it blue", document.RootElement.GetProperty("instruction").GetString());
            Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
            Assert.True(document.RootElement.GetProperty("keepRatio").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("filters").GetProperty("a").GetInt32());
            Assert.DoesNotContain("ValueKind", json);
        }

        [Fact]
        public void SerializeArguments_HandlesPlainClrValues()
        {
            var json = ToolApprovalAutoFunctionInvocationFilter.SerializeArguments(new KernelArguments
            {
                ["text"] = "hi",
                ["count"] = 3,
                ["quality"] = Quality.High,
                ["none"] = null
            });

            Assert.Equal("{\"text\":\"hi\",\"count\":3,\"quality\":\"High\",\"none\":null}", json);
        }

        [Fact]
        public async Task StoredArguments_RunABuiltInFunctionWithTypedParameters()
        {
            var plugin = new EditPlugin();
            var kernel = Kernel.CreateBuilder().Build();
            var function = kernel.Plugins.AddFromObject(plugin, "plugin_7_Image_Generation")["edit_image"];
            var stored = ToolApprovalAutoFunctionInvocationFilter.SerializeArguments(
                ModelArguments("{\"instruction\":\"make it blue\",\"count\":\"2\",\"keepRatio\":true,\"quality\":\"high\"}"));

            var result = await kernel.InvokeAsync(function, SemanticKernelAgentService.BuildArguments(stored, function), TestContext.Current.CancellationToken);

            Assert.Equal("edited", result.ToString());
            Assert.Equal("make it blue|2|True|High|<default>", plugin.Seen);
        }

        [Fact]
        public async Task StoredArguments_ReachAnMcpStyleFunctionUnchanged()
        {
            var mcp = new RecordingAIFunction();
            var kernel = Kernel.CreateBuilder().Build();
#pragma warning disable SKEXP0001
            var function = kernel.Plugins.AddFromFunctions("plugin_1_MCP", [mcp.AsKernelFunction()])["search"];
#pragma warning restore SKEXP0001
            var stored = ToolApprovalAutoFunctionInvocationFilter.SerializeArguments(
                ModelArguments("{\"q\":\"docs\",\"limit\":5,\"filters\":{\"a\":true}}"));

            await kernel.InvokeAsync(function, SemanticKernelAgentService.BuildArguments(stored, function), TestContext.Current.CancellationToken);

            Assert.Equal("docs", mcp.Received["q"]?.ToString());
            Assert.Equal("5", mcp.Received["limit"]?.ToString());
            Assert.Contains("\"a\"", mcp.Received["filters"]?.ToString());
        }

        private static SemanticKernelAgentService Service() =>
            new(Mock.Of<ILogger<SemanticKernelAgentService>>(), Mock.Of<IWkfloMapper>(), Mock.Of<IDatastreamProvider>(), Mock.Of<IEisKnowledgeProvider>(),
                Mock.Of<IChatNotificationService>(), Mock.Of<IMcpClientService>(), Mock.Of<IWkfloMediator>(), Options.Create(new ElasticsearchSettings()),
                new TenantContext(), new ChatContext(), new UserContext(), Mock.Of<IBlobStorageProvider>(), Mock.Of<IAzureSpeechProvider>(),
                Mock.Of<IDateTimeProvider>(), Mock.Of<IMasterDataDbContext>(), Mock.Of<IContactFormPluginLogService>(),
                toolApprovalStateService: Mock.Of<IToolApprovalStateService>(), toolApprovalExpiryScheduler: Mock.Of<IToolApprovalExpiryScheduler>());

        [Theory]
        [InlineData(null)] // the agent with the tool is the chat's own (root) agent
        [InlineData(10)]   // the agent with the tool was reached through a hand-off
        public async Task ApprovedEditImage_RunsWithItsStoredArguments_ForRootAndSubAgent(int? rootAgentId)
        {
            var plugin = new EditPlugin();
            var kernel = Kernel.CreateBuilder().Build();
            kernel.Plugins.AddFromObject(plugin, "plugin_7_Image_Generation");
            var agent = new TenantAgent
            {
                Id = 5,
                AgentTools = [new AgentTool { AgentId = 5, ToolId = 7, ToolPermission = "Ask", Tool = new TenantTool { Id = 7, Type = "Image_Generation", PermissionsEnabled = true, IsActive = true } }]
            };
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            record.ToolId = 7;
            record.IsMcp = false;
            record.PluginName = "plugin_7_Image_Generation";
            record.FunctionName = "edit_image";
            record.RootAgentId = rootAgentId;
            record.ArgumentsJson = ToolApprovalAutoFunctionInvocationFilter.SerializeArguments(
                ModelArguments("{\"instruction\":\"make it blue\",\"count\":1,\"keepRatio\":false,\"quality\":\"Low\",\"fileNameJson\":\"{\\\"fileName\\\":\\\"a.png\\\"}\"}"));
            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.Assistant, [new FunctionCallContent("edit_image", "plugin_7_Image_Generation", record.CallId)]));
            history.Add(new ChatMessageContent(AuthorRole.Tool, [new FunctionResultContent("edit_image", "plugin_7_Image_Generation", record.CallId, ToolApprovalHistory.AwaitingApprovalResult)]));

            await Service().ApplyToolApprovalDecisionAsync(kernel, ToolPermissionPolicy.Build(agent, approvalsAvailable: true), history, record, TestContext.Current.CancellationToken);

            Assert.Equal("make it blue|1|False|Low|{\"fileName\":\"a.png\"}", plugin.Seen);
            Assert.Equal("edited", ((FunctionResultContent)history[1].Items[0]).Result);
        }
    }
}
