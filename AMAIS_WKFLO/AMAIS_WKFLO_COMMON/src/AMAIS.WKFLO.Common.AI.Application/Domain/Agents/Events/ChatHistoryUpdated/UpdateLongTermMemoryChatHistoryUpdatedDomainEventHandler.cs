using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Mediator;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated
{
    public class UpdateLongTermMemoryChatHistoryUpdatedDomainEventHandler : INotificationHandler<ChatHistoryUpdatedDomainEvent>
    {
        private readonly IVectorStorageProvider _vectorStorageProvider;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;

        public UpdateLongTermMemoryChatHistoryUpdatedDomainEventHandler(IVectorStorageProvider vectorStorageProvider, TenantContext tenantContext, UserContext userContext)
        {
            _vectorStorageProvider = vectorStorageProvider;
            _tenantContext = tenantContext;
            _userContext = userContext;
        }

        public async ValueTask Handle(ChatHistoryUpdatedDomainEvent notification, CancellationToken cancellationToken)
        {
            // US 7502: a stopped turn adds no content: nothing new to remember.
            if (notification.IsStoppedTurn)
                return;

            if (notification.RunnableAgent.EnableLTM && !string.IsNullOrEmpty(_userContext.Guid))
            {
                // Reduce chat history to fit within token limits
                // US 7502: the "Stopped" divider is never part of what the user said or was told.
                var chatHistory = SemanticKernelExtensions.MergeConsecutiveSameRoleMessages(notification.ChatHistory.Where(m => m.Role != AuthorRole.System && !StoppedTurnMarker.IsMarker(m)));
                if (chatHistory == null || !chatHistory.Any()) return;

                IEnumerable<ChatMessageContent> contentMessages = null;
                if (chatHistory.Count() >= 4)
                {
                    var summarizationReducer = new SafeChatHistorySummarizationReducer(notification.ChatCompletionService, 1);
                    contentMessages = await summarizationReducer.ReduceAsync(chatHistory, cancellationToken);
                }
                else
                {
                    contentMessages = chatHistory;
                }

                if (contentMessages == null || !contentMessages.Any()) return;

                // Delete the current chat session
                await _vectorStorageProvider.DeleteAsync(_userContext.Guid, record => record.Categories.Contains(notification.SessionId));

                // Split message into smaller chunks for embedding
                var chunks = contentMessages.Where(m => !string.IsNullOrEmpty(m.Content)).SelectMany(m => SplitTextForEmbedding(m.Content)).ToList();

                // Create new embedding collection for this session
                await _vectorStorageProvider.AddAsync(_userContext.Guid, chunks, notification.SessionId, notification.RunnableAgent.Id, notification.RunnableAgent.Name, _tenantContext.TenantName, _userContext.Email, cancellationToken);
            }
        }

        private IEnumerable<string> SplitTextForEmbedding(string text, int maxTokensPerChunk = 500)
        {
            var words = text.Split(' ');
            var currentChunk = new List<string>();
            var tokenEstimate = 0;

            foreach (var word in words)
            {
                tokenEstimate += (int)Math.Ceiling(word.Length / 4.0);
                if (tokenEstimate > maxTokensPerChunk)
                {
                    yield return string.Join(' ', currentChunk);
                    currentChunk.Clear();
                    tokenEstimate = 0;
                }
                currentChunk.Add(word);
            }

            if (currentChunk.Count > 0)
                yield return string.Join(' ', currentChunk);
        }
    }
}
