using AMAIS.WKFLO.AI.Application.Agents.Commands.MessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.MessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Commands.TriggerMultiAgentOrchestrator;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessagePublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.VoiceMessageTenantAgentById;
using AMAIS.WKFLO.AI.Application.Agents.Models;

namespace AMAIS.WKFLO.AI.Application.Common
{
    /// <summary>
    /// Named topic constants for the <c>work-ai-request-*</c>/<c>work-ai-voice-*</c> pub/sub family —
    /// each was previously built inline via <c>$"work-ai-request-{nameof(Command)}"</c> independently
    /// at both the publisher (e.g. <c>TenantAgentChatMessage</c>) and subscriber
    /// (<c>AIWorkerSubscriber</c>/<c>VoiceWorkerSubscriber</c>/<c>AgentEventSubscriber</c>) call sites
    /// — up to 3 duplicate constructions of the same string per topic. Lives in this project (not the
    /// shared <c>AMAIS.WKFLO.Common</c> library) because the underlying command/message types are only
    /// visible here.
    /// </summary>
    public static class AiWorkerTopicConsts
    {
        public const string MessageTenantAgentByIdTopic = $"work-ai-request-{nameof(MessageTenantAgentByIdCommand)}";

        public const string MessagePublicAgentByShareIdTopic = $"work-ai-request-{nameof(MessagePublicAgentByShareIdCommand)}";

        public const string VoiceMessageTenantAgentByIdTopic = $"work-ai-voice-{nameof(VoiceMessageTenantAgentByIdCommand)}";

        public const string VoiceMessagePublicAgentByShareIdTopic = $"work-ai-voice-{nameof(VoiceMessagePublicAgentByShareIdCommand)}";

        public const string TriggerMultiAgentOrchestratorTopic = $"work-ai-request-{nameof(TriggerMultiAgentOrchestratorCommand)}";

        public const string AgentEventMessageTopic = $"work-ai-request-{nameof(AgentEventMessage)}";
    }
}
