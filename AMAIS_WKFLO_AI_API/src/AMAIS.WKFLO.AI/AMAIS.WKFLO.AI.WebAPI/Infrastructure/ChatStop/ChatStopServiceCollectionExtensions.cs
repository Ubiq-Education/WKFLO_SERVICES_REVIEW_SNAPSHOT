using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;

namespace AMAIS.WKFLO.AI.WebAPI.Infrastructure.ChatStop
{
    public static class ChatStopServiceCollectionExtensions
    {
        /// <summary>
        /// US 7501: Stop reaches every replica over Dapr pub/sub (<c>pubsub-ai-broadcast</c>, topic <c>wkflo-chat-stop</c>).
        /// Replaces the null broadcaster registered by the application layer. Needs no settings and never fails
        /// startup: a missing component only makes the publish log a warning.
        /// </summary>
        public static IServiceCollection AddChatStopBroadcast(this IServiceCollection services)
        {
            services.AddSingleton<IChatStopBroadcaster, DaprChatStopBroadcaster>();

            return services;
        }
    }
}
