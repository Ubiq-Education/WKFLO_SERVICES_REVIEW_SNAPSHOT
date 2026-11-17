using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetAgentChatSettingByShareId;
using AMAIS.WKFLO.MD.WebAPI.Models;
using AMAIS.WKFLO.MD.WebAPI.Models.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.WebAPI.Controllers
{
    public class ChatController : Controller
    {
        private readonly ChatWidgetSettings _chatWidgetSettings;
        private readonly IWkfloMediator _mediator;

        public ChatController(IOptions<ChatWidgetSettings> chatWidgetSettings, IWkfloMediator mediator)
        {
            _chatWidgetSettings = chatWidgetSettings.Value;
            _mediator = mediator;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("ShareId is required");
            }

            // Fetch agent chat settings from database using the ShareId
            var query = new GetAgentChatSettingByShareIdQuery { ShareId = id };
            var result = await _mediator.Send(query);

            AgentChatSettingResult agentChatSettings = null;

            if (result is SuccessResult<AgentChatSettingResult> successResult)
            {
                agentChatSettings = successResult.Data;
            }

            // If agent chat settings not found, return a not found result
            if (agentChatSettings == null)
            {
                return NotFound("Agent chat configuration not found for the provided ShareId.");
            }

            // Create view model combining database data with environment configuration
            var viewModel = new ChatViewModel
            {
                ShareId = id,
                TenantGuid = agentChatSettings.TenantGuid,
                AgentName = agentChatSettings.AgentName ?? "Support Agent",
                Description = agentChatSettings.AgentDescription ?? "Hi! How can I help you today?",
                HeaderTitle = agentChatSettings.HeaderTitle,
                WelcomeMessage = agentChatSettings.WelcomeMessage,
                FooterMessage = agentChatSettings.FooterMessage,
                PredefinedPrompts = agentChatSettings.PredefinedPrompts,
                PrimaryColor = agentChatSettings.PrimaryColor ?? AgentChatSettingDefaults.PrimaryColor,
                BubbleStyle = agentChatSettings.BubbleStyle ?? AgentChatSettingDefaults.BubbleStyle,
                BubbleIconPath = agentChatSettings.BubbleIconPath,
                ChatbotIconPath = agentChatSettings.ChatbotIconPath,
                CssUrl = agentChatSettings.CssUrl,
                ShowDescription = agentChatSettings.ShowDescription,
                ChainOfThought = agentChatSettings.ChainOfThought,
                IsVoiceChatEnabled = agentChatSettings.IsVoiceChatEnabled,
                CanSelectFeature = agentChatSettings.CanSelectFeature,
                IsMaisyAgent = agentChatSettings.IsMaisyAgent,
                ApiEndpoint = agentChatSettings.ApiEndpoint,
                SecurityConfiguration = agentChatSettings.SecurityConfiguration ?? new AgentSecurityConfiguration { Type = "none" },
                WidgetScriptUrl = _chatWidgetSettings.WidgetScriptUrl, // From environment variables
            };

            return View(viewModel);
        }
    }
}
