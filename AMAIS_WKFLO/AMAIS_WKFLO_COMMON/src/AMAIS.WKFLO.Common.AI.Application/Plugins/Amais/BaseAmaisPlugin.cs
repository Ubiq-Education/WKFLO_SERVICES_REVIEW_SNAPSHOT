using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Protocol;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais
{
    public abstract class BaseAmaisPlugin
    {
        protected readonly string _mcpServerId;
        protected readonly IMcpClientService _mcpClientService;
        protected readonly McpServerConfig _mcpServerConfig;
        protected readonly IDateTimeProvider _dateTimeProvider;

        protected readonly UserContext _userContext;
        protected readonly ChatContext _chatContext;

        public BaseAmaisPlugin(string mcpServerId, IMcpClientService mcpClientService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext)
            : this(mcpServerId, Environment.GetEnvironmentVariable("AMAIS_MCP_SERVER"), mcpClientService, dateTimeProvider, userContext, chatContext)
        {
        }

        /// <summary>
        /// Same as the default constructor but against a different MCP server endpoint (e.g. the DXP MCP
        /// server). The caller's CMS token is forwarded the same way.
        /// </summary>
        protected BaseAmaisPlugin(string mcpServerId, string mcpServerEndpoint, IMcpClientService mcpClientService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext)
        {
            _mcpServerId = mcpServerId;
            _userContext = userContext;
            _chatContext = chatContext;
            _mcpClientService = mcpClientService;
            _mcpServerConfig = new McpServerConfig
            {
                Id = _mcpServerId,
                Name = _mcpServerId,
                Endpoint = mcpServerEndpoint,
                TransportType = TransportType.Http,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer " + _userContext.RawToken }
                }
            };
            _mcpClientService.AddServerConfig(_mcpServerConfig);
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<List<PluginResultItem>> RunAsync(Func<Task<CallToolResult>> mcpCallback
            , Func<TextContentBlock, IDateTimeProvider, ChatMessageContent> generateMetadataMessage,
            string errorMessage = "There was an error."
            )
        {
            var results = new List<PluginResultItem>();
            var callToolResult = await McpBaseCallback(_mcpServerId, _mcpClientService, _dateTimeProvider, mcpCallback);

            if (callToolResult.IsError.GetValueOrDefault())
            {
                var contents = callToolResult.Content.Where(m => m is TextContentBlock).Cast<TextContentBlock>().ToList();
                results.Add(new PluginResultItem()
                {
                    Message = errorMessage,
                    Metadata = new Dictionary<string, object>()
                    {
                        { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                        { CommonMetadataConsts.Error, string.Join(" ", contents.Select(m => m.Text)) }
                    }
                });
            }
            else
            {
                var contents = callToolResult.Content.Where(m => m is TextContentBlock).Cast<TextContentBlock>().ToList();
                foreach (var content in contents)
                {
                    if (content.Meta.ContainsKey(CommonMetadataConsts.IsError))
                    {
                        results.Add(new PluginResultItem()
                        {
                            Message = errorMessage,
                            Metadata = new Dictionary<string, object>()
                            {
                                { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                                { CommonMetadataConsts.Error, content.Text }
                            }
                        });
                    }
                    else if (!string.IsNullOrEmpty(content.Text))
                    {
                        var metadataMessage = generateMetadataMessage(content, _dateTimeProvider);

                        // Store metadata chat message
                        if (metadataMessage != null)
                        {
                            _chatContext.AssistantPostMessagesToBeProcessed.Add(metadataMessage);
                        }

                        var result = new PluginResultItem()
                        {
                            Message = content.Text,
                            Metadata = new Dictionary<string, object>()
                        };

                        foreach (var item in content.Meta.AsEnumerable())
                        {
                            if (item.Value != null)
                            {
                                result.Metadata.Add(item.Key, item.Value.ToString());
                            }
                        }
                        results.Add(result);
                    }
                }
            }

            return results;
        }

        public Func<string, IMcpClientService, IDateTimeProvider, Func<Task<CallToolResult>>, Task<CallToolResult>> McpBaseCallback = async (string mcpServerId, IMcpClientService mcpClientService,
            IDateTimeProvider dateTimeProvider, Func<Task<CallToolResult>> mcpCallback) =>
        {
            var isConnected = await mcpClientService.ConnectAsync(mcpServerId);
            if (isConnected)
            {
                return await mcpCallback();
            }
            else
            {
                return new CallToolResult()
                {
                    IsError = true,
                    Content = new List<ContentBlock>()
                    {
                        new TextContentBlock()
                        {
                            Text = "Failed to connect to MCP server.",
                            Meta = new System.Text.Json.Nodes.JsonObject
                            {
                                [CommonMetadataConsts.Timestamp] = dateTimeProvider.DateTimeOffsetUtcNow()
                            }
                        }
                    }
                };
            }
        };

        public Func<TextContentBlock, IDateTimeProvider, ChatMessageContent> GenerateMetadataMessageWithURL = (block, dateTimeProvider) =>
        {
            var metadata = new Dictionary<string, object>()
            {
                { CommonMetadataConsts.Timestamp, dateTimeProvider.DateTimeOffsetUtcNow() },
                { CommonMetadataConsts.ChannelMessageType, ChannelMessageType.Metadata },
                { CommonMetadataConsts.RenderAsSystem, true }
            };

            if ((block.Meta?.ContainsKey(CommonMetadataConsts.Url)).GetValueOrDefault())
            {
                metadata.Add(CommonMetadataConsts.Url, block.Meta[CommonMetadataConsts.Url].ToString());
            }

            // AuthorRole.Assistant, not System: this lands in ProcessAssistantPostMessagesAsync right
            // after the assistant's plain-text response for the turn (not one ending in a server tool
            // result), which is where Anthropic rejects a 'system' role message. It's also the agent's
            // own supplementary output (e.g. a source link), not operator/background content, so
            // Assistant is the correct role either way - the API merges consecutive assistant turns
            // rather than rejecting them.
            return new ChatMessageContent(AuthorRole.Assistant, block.Text, metadata: metadata);
        };

        public Func<TextContentBlock, IDateTimeProvider, ChatMessageContent> GenerateMetadataMessageEmpty = (block, dateTimeProvider) =>
        {
            return null;
        };
    }
}
