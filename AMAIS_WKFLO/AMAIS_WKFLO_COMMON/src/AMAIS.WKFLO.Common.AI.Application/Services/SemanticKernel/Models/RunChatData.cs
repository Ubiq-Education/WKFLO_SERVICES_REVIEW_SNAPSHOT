using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models
{
    public class RunChatData
    {
        public Kernel Kernel { get; set; }
        public RunnableAgent Agent { get; set; }
        public List<string> AgentTagNames { get; set; }
        public string Prompt { get; set; }
        public string Message { get; set; }
        public List<FileAttachment> Files { get; set; } = [];
        public string SessionId { get; set; }
        public ChatHistoryAgentThread ChatHistoryAgentThread { get; set; }
        public string ChatName { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string AssitantReasoning { get; set; }

        /// <summary>
        /// US 7974: the timestamp the streamed "thinking" bubble of <see cref="AssitantReasoning"/> carried; persisted
        /// with that message so the reloaded history shows the same time. Null when no reasoning was streamed.
        /// </summary>
        public DateTimeOffset? AssitantReasoningTimestamp { get; set; }
        public string ImageAnalysis { get; set; }
        public Guid UserMessageId { get; set; }

        /// <summary>
        /// US 7974: when the turn received the user's message, taken before any reasoning is streamed so the persisted
        /// question is never later than its own "thinking" bubble. Null: the time the message is added to the history.
        /// </summary>
        public DateTimeOffset? UserMessageTimestamp { get; set; }
        public string TopicName { get; set; }
        public string TopicHandoffMessage { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.RootIsWidget"/> — forwarded from there into this run's icon
        /// resolution.
        /// </summary>
        public bool? RootIsWidget { get; set; }

        /// <summary>
        /// Whether Amais skills are enabled for this run (computed once per run via
        /// <c>kernel.AddWkflowToolsAsync(...)</c>). Gates whether the Amais CMS page/feature integration
        /// context line (<c>SemanticKernelAgentService.BuildAmaisContextLine</c>) is spliced into the prompt.
        /// Defaults to <c>false</c> so the context line is omitted unless explicitly enabled.
        /// </summary>
        public bool AmaisSkillsEnabled { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.IsMaisyAgent"/> — forwarded from there and consumed by
        /// <c>SemanticKernelAgentService.BuildChatCompletionAgentInstructions</c> to decide whether the
        /// model may enumerate its tools/plugins when asked a meta/introspection question about them.
        /// </summary>
        public bool IsMaisyAgent { get; set; }

        /// <summary>
        /// See <see cref="RunTextOptions.IsContactFormReplay"/> — forwarded from there. When true,
        /// <c>RunChatCompletionAgentAsync</c> skips adding a new <c>AuthorRole.User</c> message for
        /// <see cref="Message"/> (already recorded by the original turn this replays), preserving the
        /// ContactForm tool's filler assistant reply from that original turn as-is, and instead appends
        /// an ephemeral (never persisted) replay cue so the completion request stays well-formed.
        /// </summary>
        public bool IsContactFormReplay { get; set; }

        /// <summary>WI 7447/7575: effective tool permissions of this run (prompt injections skip blocked tools).</summary>
        public Domain.Agents.ToolPermissions.ToolPermissionPolicy ToolPermissionPolicy { get; set; }

        /// <summary>WI 7575: the approval filter of this run (null when nobody can approve).</summary>
        public Filters.ToolApprovalAutoFunctionInvocationFilter ToolApprovalFilter { get; set; }

        /// <summary>
        /// WI 7575: resume turn after a tool approval decision. Like <see cref="IsContactFormReplay"/>, no new user
        /// message is recorded; <see cref="Message"/> carries the ephemeral cue sent to the model instead.
        /// </summary>
        public bool IsToolApprovalResume { get; set; }

        /// <summary>US 7501: set once the model's answer starts streaming, so a Stop is logged with the phase it hit.</summary>
        public bool StreamingStarted { get; set; }

        /// <summary>
        /// US 7502: "Stopped" markers removed from <see cref="ChatHistoryAgentThread"/> when the history was loaded, so the
        /// model never sees them. Put back before the history is saved (see BuildPersistableChatHistory).
        /// </summary>
        public IReadOnlyList<ChatMessageContent> DetachedStoppedMarkers { get; set; } = [];

        /// <summary>
        /// US 7502: set once this turn's history was handed to the save (a normal save or the stopped turn's own save).
        /// A Stop caught by the caller then never saves it a second time (see SaveEarlyStoppedTurnAsync).
        /// </summary>
        public bool TurnSaved { get; set; }

        /// <summary>US 7502: what happened to the "Stopped" marker when this turn was stopped while streaming (for the logs).</summary>
        public Domain.Agents.ChatStop.StoppedMarkerOutcome? StoppedMarkerOutcome { get; set; }
    }
}
