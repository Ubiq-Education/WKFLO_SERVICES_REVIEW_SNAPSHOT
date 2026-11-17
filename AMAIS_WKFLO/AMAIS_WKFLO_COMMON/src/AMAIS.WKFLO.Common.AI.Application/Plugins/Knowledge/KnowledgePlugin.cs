using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.Results.Knowledge;
using AMAIS.WKFLO.Common.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Knowledge
{
    public class KnowledgePlugin
    {
        private readonly Tool _tool;
        private readonly IDatastreamProvider _datastreamProvider;
        private readonly IChatNotificationService _notificationService;
        private readonly ElasticsearchSettings _elasticsearchSettings;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly ILogger _logger;

        public KnowledgePlugin(Tool tool, IDatastreamProvider datastreamProvider, IChatNotificationService notificationService,
            IOptions<ElasticsearchSettings> settings, TenantContext tenantContext, ChatContext chatContext, ILogger logger)
        {
            _tool = tool;
            _datastreamProvider = datastreamProvider;
            _notificationService = notificationService;
            _elasticsearchSettings = settings.Value;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _logger = logger;
        }

        [KernelFunction("get_relevant_knowledge_documents")]
        [Description("Gets relevant document knowledge")]
        public async Task<string> GetRelevantDocumentFromKnowledgeAsync([Description("User message")] string message)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<KnowledgeToolConfig>(_tool.Config);
                var results = await _datastreamProvider.SearchAllKnowledgeAsync<BaseKnowledgeResult>(message, config.Tags, 20, true, _tenantContext.TenantName, _elasticsearchSettings.KnowledgeIndex);
                return string.Join("\n" + new string('-', 50) + "\n", results.Select((m, index) => $"DOCUMENTO {index + 1}:\n{m.Text}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing KnowledgePlugin for tool {ToolName}", _tool.Name);
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    "Searching for knowledge information failed...",
                    statusCode: 500
                );

                return string.Empty;
            }
        }
    }
}
