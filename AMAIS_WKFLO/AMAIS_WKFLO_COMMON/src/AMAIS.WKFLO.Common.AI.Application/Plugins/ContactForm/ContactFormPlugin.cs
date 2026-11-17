using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Services;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Config;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.ContactForm
{
    public class ContactFormPlugin
    {
        private readonly Tool _tool;
        private readonly IChatNotificationService _notificationService;
        private readonly ChatContext _chatContext;
        private readonly IContactFormPluginLogService _contactFormLogService;
        private readonly string _tenantName;
        private readonly ILogger _logger;

        public ContactFormPlugin(Tool tool, IChatNotificationService notificationService, ChatContext chatContext,
            ILogger logger, IContactFormPluginLogService contactFormLogService = null, string tenantName = null)
        {
            _tool = tool;
            _notificationService = notificationService;
            _chatContext = chatContext;
            _contactFormLogService = contactFormLogService;
            _tenantName = tenantName;
            _logger = logger;
        }

        [Description("User message or question")]
        public async Task<string> RequestContactFormAsync(string message)
        {
            try
            {
                var config = JsonConvert.DeserializeObject<ContactFormToolConfig>(_tool.Config);
                if (config == null)
                    return "Contact form configuration is missing.";

                if (_contactFormLogService != null
                    && !string.IsNullOrEmpty(_tenantName)
                    && !string.IsNullOrEmpty(_chatContext.SubjectId)
                    && Guid.TryParse(_chatContext.SubjectId, out var subjectGuid))
                {
                    var alreadyExists = await _contactFormLogService.ExistsAsync(subjectGuid, _tenantName);
                    if (alreadyExists)
                        return "Contact information has already been collected for this user. Do not request registration. Answer the question directly.";
                }

                var update = new ContactFormUpdate
                {
                    ChatId = _chatContext.SessionId,
                    Message = message ?? string.Empty,
                    Mandatory = config.Mandatory,
                    FirstNameRequired = config.FirstNameRequired,
                    LastNameRequired = config.LastNameRequired,
                    EmailRequired = config.EmailRequired,
                    SubmitButtonText = string.IsNullOrWhiteSpace(config.SubmitButtonText) ? "Submit" : config.SubmitButtonText,
                    SuccessMessageEnabled = config.SuccessMessageEnabled,
                    SuccessMessageText = config.SuccessMessageText
                };

                await _notificationService.SendContactFormRequestAsync(_chatContext.SessionId, update);

                // US 7142 corner case: this turn's assistant response is about to be just this filler
                // text, not a real answer - flagged so RunChatCompletionAgentAsync skips scheduling
                // MessageResolutionWorkflow for it (see ChatContext.ContactFormRequestedThisTurn's own
                // doc comment). Deliberately NOT set on the "already collected" short-circuit above,
                // since that path tells the model to answer the question directly in this same turn.
                _chatContext.ContactFormRequestedThisTurn = true;

                return config.Mandatory
                    ? "A registration form has been displayed to the user. To access this information, registration is required. Do not add any other explanation or assumption about why registration is needed beyond what is stated in your instructions."
                    : "A registration form has been displayed to the user. Registration is optional to continue. Do not add any other explanation or assumption about why registration is needed beyond what is stated in your instructions.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing ContactFormPlugin for tool {ToolName}", _tool.Name);
                await _notificationService.SendToolExecutionUpdate(
                    _chatContext.SessionId,
                    _tool.Name,
                    "failed",
                    "Loading contact form failed...",
                    statusCode: 500
                );

                return string.Empty;
            }
        }
    }
}
