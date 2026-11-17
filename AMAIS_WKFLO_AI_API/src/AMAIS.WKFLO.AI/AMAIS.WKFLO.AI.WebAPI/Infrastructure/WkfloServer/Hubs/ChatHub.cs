using Microsoft.AspNetCore.SignalR;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.WkfloServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ILogger<ChatHub> logger)
        {
            _logger = logger;
        }

        public async Task JoinChat(string chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId);
            _logger.LogInformation("User {UserId} joined chat {ChatId}", Context.UserIdentifier, chatId);
        }

        public async Task LeaveChat(string chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId);
            _logger.LogInformation("User {UserId} left chat {ChatId}", Context.UserIdentifier, chatId);
        }

        public async Task SendTypingIndicator(string chatId, bool isTyping, string userName)
        {
            // Broadcast typing indicator to other users in the chat (excluding sender)
            await Clients.GroupExcept(chatId, Context.ConnectionId).SendAsync("TypingIndicator", new
            {
                chatId,
                isTyping,
                userName
            });
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("User {UserId} connected to SignalR hub", Context.UserIdentifier);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation("User {UserId} disconnected: {Error}", Context.UserIdentifier, exception?.Message);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
