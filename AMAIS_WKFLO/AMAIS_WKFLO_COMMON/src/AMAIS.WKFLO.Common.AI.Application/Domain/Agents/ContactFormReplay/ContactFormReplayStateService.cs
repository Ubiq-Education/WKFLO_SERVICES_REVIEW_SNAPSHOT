using AMAIS.WKFLO.Common.Models;
using Dapr.Client;

namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ContactFormReplay
{
    public class ContactFormReplayStateService : IContactFormReplayStateService
    {
        // Generous enough for a visitor to read/fill/submit the contact form without the pending state
        // becoming a stale, indefinitely-valid replay window if they never actually complete it.
        private const int PendingReplayTtlSeconds = 1800;

        // Same window as PendingReplayTtlSeconds - this record only needs to survive from "form requested"
        // to "form submitted", the same visitor action the pending-replay window itself is sized for.
        private const int DeferredQuestionTtlSeconds = 1800;

        private readonly DaprClient _daprClient;

        public ContactFormReplayStateService(DaprClient daprClient)
        {
            _daprClient = daprClient;
        }

        public Task RecordDeferredQuestionAsync(string sessionId, Guid messageId, CancellationToken cancellationToken = default)
        {
            return _daprClient.SaveStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildDeferredQuestionKey(sessionId),
                messageId,
                metadata: new Dictionary<string, string> { ["ttlInSeconds"] = DeferredQuestionTtlSeconds.ToString() },
                cancellationToken: cancellationToken);
        }

        public async Task SetPendingReplayAsync(string sessionId, string originalMessage, CancellationToken cancellationToken = default)
        {
            // Best-effort - a missing/expired deferred-question record just means OriginalMessageId stays
            // null (the replay call falls back to a freshly-generated MessageId, same as before this
            // existed), never a reason to fail recording the pending replay itself.
            var deferredMessageId = await _daprClient.GetStateAsync<Guid?>(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildDeferredQuestionKey(sessionId),
                cancellationToken: cancellationToken);

            await _daprClient.SaveStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildKey(sessionId),
                new ContactFormReplayPendingState { OriginalMessage = originalMessage, OriginalMessageId = deferredMessageId, CreatedAt = DateTimeOffset.UtcNow },
                metadata: new Dictionary<string, string> { ["ttlInSeconds"] = PendingReplayTtlSeconds.ToString() },
                cancellationToken: cancellationToken);

            // Best-effort cleanup, once the deferred question has been merged into the pending replay above -
            // without this, a stale OriginalMessageId can linger for up to DeferredQuestionTtlSeconds and get
            // picked up by a LATER, unrelated ContactForm request in the same session (found via a Copilot PR
            // review). A delete failure here is never fatal: the key's own TTL still bounds how long it can
            // linger, same as before this cleanup existed.
            try
            {
                await _daprClient.DeleteStateAsync(
                    CommonConsts.CommonAiPubSubConsts.StateStoreName,
                    BuildDeferredQuestionKey(sessionId),
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // Swallow - see comment above.
            }
        }

        public Task<ContactFormReplayPendingState> GetPendingReplayAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _daprClient.GetStateAsync<ContactFormReplayPendingState>(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildKey(sessionId),
                cancellationToken: cancellationToken);
        }

        public Task ConsumePendingReplayAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _daprClient.DeleteStateAsync(
                CommonConsts.CommonAiPubSubConsts.StateStoreName,
                BuildKey(sessionId),
                cancellationToken: cancellationToken);
        }

        private static string BuildKey(string sessionId) => $"contact-form-replay-{sessionId}";
        private static string BuildDeferredQuestionKey(string sessionId) => $"contact-form-deferred-question-{sessionId}";
    }
}
