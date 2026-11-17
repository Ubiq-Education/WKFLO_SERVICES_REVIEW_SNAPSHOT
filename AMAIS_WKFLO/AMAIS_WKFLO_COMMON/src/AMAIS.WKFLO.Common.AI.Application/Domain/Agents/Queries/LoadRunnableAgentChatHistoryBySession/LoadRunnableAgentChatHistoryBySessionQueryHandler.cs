using AMAIS.WKFLO.Common.AI.Application.Extensions;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services;
using Mediator;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession
{
    public class LoadRunnableAgentChatHistoryBySessionQueryHandler : IRequestHandler<LoadRunnableAgentChatHistoryBySessionQuery, (DateTimeOffset, string, ChatHistory)>
    {
        private readonly IChatStoreService _chatStoreService;
        private readonly TenantContext _tenantContext;
        private readonly UserContext _userContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public LoadRunnableAgentChatHistoryBySessionQueryHandler(IChatStoreService chatStoreService, TenantContext tenantContext, UserContext userContext, IDateTimeProvider dateTimeProvider)
        {
            _chatStoreService = chatStoreService;
            _tenantContext = tenantContext;
            _userContext = userContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<(DateTimeOffset, string, ChatHistory)> Handle(LoadRunnableAgentChatHistoryBySessionQuery query, CancellationToken cancellationToken)
        {
            if (query.Agent.EnableSTM)
            {
                var (creationAt, name, chatHistory, _) = await _chatStoreService.LoadChatAsync(_tenantContext.TenantName, query.Agent.Id, query.SessionId, _userContext.Guid);
                SemanticKernelExtensions.RemoveMidConversationSystemMessages(chatHistory);
                return (creationAt, name, chatHistory);
            }
            else
            {
                return (_dateTimeProvider.DateTimeOffsetUtcNow(), null, []);
            }
        }
    }
}
