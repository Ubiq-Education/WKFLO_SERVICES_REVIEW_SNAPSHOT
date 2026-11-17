using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Text;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.PriorityQA
{
    public class PriorityQAPlugin
    {
        private readonly Tool _tool;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly ILogger _logger;

        public PriorityQAPlugin(Tool tool, IChatNotificationService notificationService, ChatContext chatContext, ILogger logger)
        {
            _tool = tool;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _logger = logger;
        }

        [KernelFunction("get_priority_qa_answers")]
        [Description("PRIORITY SOURCE: Always consult this BEFORE any other knowledge source or tool. Contains authoritative predefined Q&A pairs. If the user's question matches any entry here, use that answer directly and do not search elsewhere.")]
        public async Task<string> GetPriorityQAAnswersAsync([Description("User message or question")] string message)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<PriorityQAToolConfig>(_tool.Config);

                if (config?.Items == null || config.Items.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("PRIORITY Q&A KNOWLEDGE BASE — Use these answers first:");
                sb.AppendLine(new string('=', 60));

                foreach (var (item, index) in config.Items.Select((item, i) => (item, i + 1)))
                {
                    sb.AppendLine($"Q{index}: {item.Question}");
                    sb.AppendLine($"A{index}: {item.Answer}");

                    if (item.Links?.Count > 0)
                    {
                        sb.AppendLine($"Additional links: {string.Join(", ", item.Links)}");
                    }

                    sb.AppendLine(new string('-', 50));
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing PriorityQAPlugin for tool {ToolName}", _tool.Name);
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    "Loading priority Q&A knowledge failed...",
                    statusCode: 500
                );

                return string.Empty;
            }
        }
    }
}
