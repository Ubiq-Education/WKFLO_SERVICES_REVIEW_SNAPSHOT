namespace AMAIS.WKFLO.MD.WebAPI.Models.Settings
{
    /// <summary>
    /// Settings for the Chat Widget configuration that come from environment variables
    /// </summary>
    public class ChatWidgetSettings
    {
        /// <summary>
        /// The widget script URL for loading the chat widget
        /// </summary>
        public string WidgetScriptUrl { get; set; } = "http://localhost/assets/wkflo-widget.umd.cjs";
    }
}