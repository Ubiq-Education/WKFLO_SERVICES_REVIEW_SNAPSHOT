using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated
{
    public class ChatHistoryUpdatedDomainEvent : IWkfloNotification
    {
        public RunnableAgent RunnableAgent { get; set; }
        public string SessionId { get; set; }
        public string ChatName { get; set; }
        public ChatHistory ChatHistory { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public IChatCompletionService ChatCompletionService { get; set; }

        /// <summary>
        /// US 7502: the save of a turn the user stopped: the history as it was before that turn plus the "Stopped" marker.
        /// It adds no content, so the conversation is not analysed again, its analysis status is left as it was, and
        /// long-term memory is not updated.
        /// </summary>
        public bool IsStoppedTurn { get; set; }

        public ChatHistoryUpdatedDomainEvent(RunnableAgent runnableAgent, string sessionId, string name, DateTimeOffset createdAt, DateTimeOffset updatedAt,
            ChatHistory chatHistory, IChatCompletionService chatCompletionService)
        {
            RunnableAgent
                = runnableAgent;
            SessionId = sessionId;
            ChatName = name;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            ChatHistory = chatHistory;
            ChatCompletionService = chatCompletionService;
        }
    }
}

