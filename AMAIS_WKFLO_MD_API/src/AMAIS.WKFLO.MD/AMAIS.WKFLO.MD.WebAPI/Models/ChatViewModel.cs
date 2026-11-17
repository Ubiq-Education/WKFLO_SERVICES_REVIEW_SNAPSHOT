using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.Agents.Models;

namespace AMAIS.WKFLO.MD.WebAPI.Models
{
    /// <summary>
    /// View model for the Chat page containing agent chat settings data
    /// </summary>
    public class ChatViewModel
    {
        /// <summary>
        /// The share ID for the agent chat settings
        /// </summary>
        public string ShareId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unique identifier for the tenant.
        /// </summary>
        /// <remarks>This GUID is used to distinguish between different tenants in a multi-tenant
        /// application. Ensure that the value is a valid GUID format.</remarks>
        public string TenantGuid { get; set; }

        /// <summary>
        /// Agent name for display purposes
        /// </summary>
        public string AgentName { get; set; }

        /// <summary>
        /// Agent description for display purposes
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Header Title shown when the chat opens
        /// </summary>
        public string HeaderTitle { get; set; }

        /// <summary>
        /// Welcome message shown when the chat opens
        /// </summary>
        public string WelcomeMessage { get; set; }

        /// <summary>
        /// Footer message (e.g. AI disclaimer) shown below the chat input
        /// </summary>
        public string FooterMessage { get; set; }

        /// <summary>
        /// Quick-reply prompts shown to the user when the chat opens
        /// </summary>
        public IList<PredefinedPromptModel> PredefinedPrompts { get; set; } = [];

        /// <summary>
        /// Primary color for the chat widget styling
        /// </summary>
        public string PrimaryColor { get; set; } = AgentChatSettingDefaults.PrimaryColor;

        /// <summary>
        /// Bubble style for the chat widget (e.g., "rounded", "square")
        /// </summary>
        public string BubbleStyle { get; set; } = AgentChatSettingDefaults.BubbleStyle;

        /// <summary>
        /// Icon path for the chat bubble button
        /// </summary>
        public string BubbleIconPath { get; set; }

        /// <summary>
        /// Icon path for the chatbot avatar
        /// </summary>
        public string ChatbotIconPath { get; set; }

        /// <summary>
        /// Custom CSS URL for the widget
        /// </summary>
        public string CssUrl { get; set; }

        /// <summary>
        /// Whether to show the description in the widget
        /// </summary>
        public bool ShowDescription { get; set; } = AgentChatSettingDefaults.ShowDescription;

        /// <summary>
        /// Gets or sets a value indicating whether the chain of thought process is enabled
        /// </summary>
        public bool ChainOfThought { get; set; } = AgentChatSettingDefaults.ChainOfThought;

        /// <summary>
        /// Gets or sets a value indicating whether the voice chat is enabled
        /// </summary>
        public bool IsVoiceChatEnabled { get; set; } = AgentChatSettingDefaults.IsVoiceChatEnabled;

        /// <summary>
        /// Gets or sets a value indicating whether the agent is tagged "Maisy", allowing the widget's
        /// feature-selection button to be shown
        /// </summary>
        public bool CanSelectFeature { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the agent is tagged "Maisy" (gates the MAISY-only widget behaviour)
        /// </summary>
        public bool IsMaisyAgent { get; set; }

        /// <summary>
        /// The widget script URL for loading the chat widget
        /// </summary>
        public string WidgetScriptUrl { get; set; }

        /// <summary>
        /// API endpoint for the chat agent
        /// </summary>
        public string ApiEndpoint { get; set; }

        /// <summary>
        /// Target element ID for the widget container
        /// </summary>
        public string TargetElementId { get; set; } = "widget-chat";

        /// <summary>
        /// Container ID for the widget wrapper
        /// </summary>
        public string ContainerId { get; set; } = "widget-container-wrapper";

        /// <summary>
        /// Indicates whether the ShareId is valid and not empty
        /// </summary>
        public bool HasValidShareId => !string.IsNullOrEmpty(ShareId);

        /// <summary>
        /// Security Configuration
        /// </summary>
        public AgentSecurityConfiguration SecurityConfiguration { get; set; }
    }
}