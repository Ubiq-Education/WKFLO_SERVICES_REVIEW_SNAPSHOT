using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Events.ChatHistoryUpdated;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.Queries.LoadRunnableAgentChatHistoryBySession;
using AMAIS.WKFLO.Common.AI.Application.Helpers;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI.Providers;
using AMAIS.WKFLO.Common.Models.Results;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.Application.Agents.Commands.StoreMessagePublicAgentByShareId
{
    public class StoreMessagePublicAgentByShareIdCommandHandler : IRequestHandler<StoreMessagePublicAgentByShareIdCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMediator _mediator;
        private readonly IChatNotificationService _notificationService;
        private readonly TenantContext _tenantContext;
        private readonly ChatContext _chatContext;
        private readonly IDateTimeProvider _dateTimeProvider;

        public StoreMessagePublicAgentByShareIdCommandHandler(IMasterDataDbContext db, IWkfloMediator mediator, IChatNotificationService notificationService,
            TenantContext tenantContext, ChatContext chatContext, IDateTimeProvider dateTimeProvider)
        {
            _db = db;
            _mediator = mediator;
            _notificationService = notificationService;
            _tenantContext = tenantContext;
            _chatContext = chatContext;
            _dateTimeProvider = dateTimeProvider;
        }

        public async ValueTask<Result> Handle(StoreMessagePublicAgentByShareIdCommand command, CancellationToken cancellationToken)
        {
            // Check AgentChatSetting
            var chatSetting = await _db.AgentChatSettings
                .Include(acs => acs.Agent)
                .Include(m => m.Agent.AIModel)
                .ThenInclude(m => m.Secret)
                .Include(m => m.Agent.AgentTools)
                .ThenInclude(m => m.Tool)
                .Where(acs => acs.ShareId == command.ShareId && !acs.IsDeleted && !acs.Agent.IsDeleted && acs.Agent.Tenant.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            // Find the runnable agent (which is publicly accessible)
            var agent = chatSetting?.Agent;

            if (agent != null && agent is RunnableAgent runnableAgent && runnableAgent.IsActive)
            {
                // Get AI provider for the agent
                AIBaseProvider provider = AIProviderHelper.GetProvider(runnableAgent);

                // Get kernel for provider
                Kernel kernel = AIProviderHelper.GetKernel(provider, _notificationService, _dateTimeProvider, _chatContext);

                // Get chat completion service
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Load chat history
                var (createdAt, chatName, chatHistory) = await _mediator.Send(new LoadRunnableAgentChatHistoryBySessionQuery()
                {
                    Agent = runnableAgent,
                    SessionId = command.SessionId
                }, cancellationToken);

                var chatHistoryAgentThread = new ChatHistoryAgentThread(chatHistory);

                // Message Id
                var messageId = Guid.NewGuid().ToString();

                // Add new user message
                var metadata = new Dictionary<string, object>
                {
                    { CommonMetadataConsts.Timestamp, _dateTimeProvider.DateTimeOffsetUtcNow() },
                    { CommonMetadataConsts.MessageId, messageId }
                };

                var messageContent = new ChatMessageContentItemCollection { new TextContent(command.Input.Text) };

                if (command.Input.Type == "user")
                {
                    chatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.User, messageContent, metadata: metadata);
                }
                else
                {
                    chatHistoryAgentThread.ChatHistory.AddMessage(AuthorRole.Assistant, messageContent, metadata: metadata);
                }

                await _mediator.Publish(new ChatHistoryUpdatedDomainEvent
                    (
                        runnableAgent,
                        command.SessionId,
                        chatName,
                        createdAt,
                        _dateTimeProvider.DateTimeOffsetUtcNow(),
                        chatHistoryAgentThread.ChatHistory,
                        chatCompletionService
                    ), cancellationToken);

                // Delete the thread when it is no longer needed
                await chatHistoryAgentThread.DeleteAsync();
            }

            return new SuccessResult<bool>(true);
        }
    }
}
