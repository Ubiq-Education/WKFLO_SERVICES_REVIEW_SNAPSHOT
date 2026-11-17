using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.Mcp;
using AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.TextFeature
{
    public sealed class TextFeaturePlugin : BaseAmaisPlugin
    {
        internal const string GetTextFeatureToolName = "get_text_feature";

        public TextFeaturePlugin(IMcpClientService mcpService, IDateTimeProvider dateTimeProvider, UserContext userContext, ChatContext chatContext)
            : base("AMAIS MCP Text Feature plugin", mcpService, dateTimeProvider, userContext, chatContext)
        {
        }

        [KernelFunction(GetTextFeatureToolName)]
        [Description("Retrieve the saved and published text content for a specific feature by its ID. Returns feature ID, status, saved text, published text, and published date.")]
        public async Task<List<PluginResultItem>> GetTextFeature(
            [Description("ID of the feature to retrieve the text content for.")] int featureId)
        {
            return await RunAsync(
                async () =>
                {
                    return await _mcpClientService.CallToolAsync(_mcpServerId, GetTextFeatureToolName, new Dictionary<string, object>()
                    {
                        { "id", featureId }
                    }, CancellationToken.None);
                },
                GenerateMetadataMessageEmpty,
                "There was an error retrieving the feature text data."
             );
        }
    }
}
