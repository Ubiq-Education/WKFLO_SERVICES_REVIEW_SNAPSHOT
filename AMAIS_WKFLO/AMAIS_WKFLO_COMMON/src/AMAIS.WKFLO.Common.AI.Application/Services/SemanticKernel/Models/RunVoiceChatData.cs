using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    public class RunVoiceChatData
    {
        public Kernel Kernel { get; set; }
        public RunnableAgent Agent { get; set; }
        public List<string> AgentTagNames { get; set; }
        public string Prompt { get; set; }
        public string Message { get; set; }
        public string SessionId { get; set; }
        public ChatHistoryAgentThread ChatHistoryAgentThread { get; set; }
        public string ChatName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string BranchId { get; set; }
        public string Voice { get; set; }
        public string Language { get; set; }
        public string AssitantReasoning { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.RootIsWidget"/> — forwarded from <see cref="RunVoiceOptions"/>
        /// into this run's icon resolution.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// Generated once in <c>SemanticKernelAgentService.RunVoiceAsync</c> and shared between the
        /// classification record and this turn's persisted user message - mirrors <see cref="RunChatData.UserMessageId"/>.
        /// Voice has no ContactForm-replay concept (no upstream command carries an OriginalMessageId
        /// equivalent), so this is always a freshly-generated id, never a reused/original one.
        /// </summary>
        public Guid UserMessageId { get; set; }

        /// <summary>
        /// See <see cref="RunChatData.TopicName"/> - the topic this turn's question was classified under
        /// (if the agent has a ChatClassification tool configured), stamped onto the persisted user
        /// message's metadata.
        /// </summary>
        public string TopicName { get; set; }

        /// <summary>
        /// See <see cref="RunChatData.TopicHandoffMessage"/> - spliced into the system prompt as a handoff
        /// instruction when the classified topic has one configured.
        /// </summary>
        public string TopicHandoffMessage { get; set; }

        /// <summary>
        /// Whether Amais skills are enabled for this run (computed once per run via
        /// <c>kernel.AddWkflowToolsAsync(...)</c>). Gates whether the Amais CMS page/feature integration
        /// context line (<c>SemanticKernelAgentService.BuildAmaisContextLine</c>) is spliced into the prompt.
        /// Defaults to <c>false</c> so the context line is omitted unless explicitly enabled.
        /// </summary>
        public bool AmaisSkillsEnabled { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.IsMaisyAgent"/> — forwarded from <see cref="RunVoiceOptions"/> and
        /// consumed by <c>SemanticKernelAgentService.BuildChatCompletionAgentInstructions</c> to decide
        /// whether the model may enumerate its tools/plugins when asked a meta/introspection question
        /// about them.
        /// </summary>
        public bool IsMaisyAgent { get; set; }

        /// <summary>WI 7447/7575: effective tool permissions of this run (voice: Ask behaves as Block).</summary>
        public Domain.Agents.ToolPermissions.ToolPermissionPolicy ToolPermissionPolicy { get; set; }

        /// <summary>
        /// US 7502: "Stopped" markers removed from <see cref="ChatHistoryAgentThread"/> when the history was loaded, so the
        /// model never sees them. Put back before the history is saved (see BuildPersistableChatHistory).
        /// </summary>
        public IReadOnlyList<ChatMessageContent> DetachedStoppedMarkers { get; set; } = [];
    }
}
