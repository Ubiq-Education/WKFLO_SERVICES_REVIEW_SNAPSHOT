using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Welcome
{
    public sealed class WelcomePlugin : BaseAmaisPlugin
    {
        private const string WelcomeToolName = "get_welcome_message";

        public WelcomePlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext)
            : base("AMAIS MCP Welcome plugin", mcpService, dateTimeProvider, userContext, chatContext)
        {
        }

        [KernelFunction(WelcomeToolName)]
        [Description("Get Welcome Message")]
        public async Task<List<PluginResultItem>> GetWelcomeMessage()
        {
            return await RunAsync(
                async () =>
                {
                    var callToolResult = await _mcpClientService.CallToolAsync(_mcpServerId, WelcomeToolName, null, CancellationToken.None);
                    return callToolResult;
                },
                (block, dateTimeProvider) =>
                {
                    // AuthorRole.Assistant, not System: see BaseAmaisPlugin.GenerateMetadataMessageWithURL
                    // for why (ProcessAssistantPostMessagesAsync appends this after the assistant's
                    // plain-text response, where Anthropic rejects a 'system' role message).
                    var metadataMessage = new ChatMessageContent(AuthorRole.Assistant, block.Text, metadata: new Dictionary<string, object>()
                    {
                        { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                        { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Metadata },
                        { CommonMetadataConsts.RenderAsSystem, true }
                    });
                    return metadataMessage;
                },
                "There was an error getting the welcome message."
             );
        }
    }
}
