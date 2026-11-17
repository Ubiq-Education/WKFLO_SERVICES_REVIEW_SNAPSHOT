using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Models;
using Dapr;
using Microsoft.AspNetCore.Mvc;

namespace AMAIS.WKFLO.AI.WebAPI.EndPoints.Dapr
{
    /// <summary>
    /// US 7501: every AI API replica receives every Stop (<c>pubsub-ai-broadcast</c> has a consumer id per replica) and
    /// cancels its own turns of that session that started before it. Always answers success, also for a session this
    /// replica isn't running or a malformed message, so a Stop is never retried or dead-lettered.
    /// </summary>
    [ApiController]
    public class ChatStopSubscriber : ControllerBase
    {
        [Topic(CommonConsts.CommonAiBroadcastPubSubConsts.PubSubName, CommonConsts.CommonAiBroadcastPubSubConsts.ChatStopTopic)]
        [HttpPost($"/{CommonConsts.CommonAiBroadcastPubSubConsts.ChatStopTopic}")]
        public ActionResult ChatStopCall(
            CloudEvent<ChatStopSignal> cloudEvent,
            [FromServices] ChatStopBroadcastHandler handler,
            [FromServices] ILogger<ChatStopSubscriber> logger)
        {
            try
            {
                handler.Handle(cloudEvent?.Data);
            }
            catch (Exception ex)
            {
                // Nothing to retry: a Stop that cannot be applied here is still caught by the checkpoint reads.
                logger.LogError(ex, "Applying a chat stop broadcast failed");
            }

            return Ok();
        }
    }
}
