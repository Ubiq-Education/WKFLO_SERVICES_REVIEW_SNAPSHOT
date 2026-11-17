namespace AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959: the failure of the chat turn running in this scope (one per workflow activity scope). Lower layers record
    /// what went wrong here instead of messaging the chat themselves; the turn's single choke point reads it and notifies
    /// the chat once (see <see cref="IChatTurnErrorNotifier"/>).
    /// </summary>
    public interface IChatTurnFailureState
    {
        bool HasFailure { get; }

        /// <summary>The recorded failure, or null.</summary>
        ChatTurnFailure Failure { get; }

        /// <summary>The exception behind <see cref="Failure"/>, for the logs only. Never sent to the chat.</summary>
        Exception Exception { get; }

        /// <summary>
        /// Records <paramref name="failure"/>. The first terminal failure wins; a warning is only kept while nothing else
        /// is recorded and is replaced by a later terminal failure. Returns true when it was recorded.
        /// </summary>
        bool Record(ChatTurnFailure failure, Exception exception = null);

        /// <summary>Shortcut for <see cref="Record(ChatTurnFailure, Exception)"/> with <see cref="ChatTurnFailure.Create"/>.</summary>
        bool Record(string code, int? agentId = null, string agentName = null, string chatbotIconUrl = null, string detailCode = null, Exception exception = null);

        /// <summary>
        /// True once this turn's answer reached the chat in full (Task 7961). A terminal failure recorded after that is
        /// not reported: the user already has the answer.
        /// </summary>
        bool AnswerDelivered { get; }

        /// <summary>Marks the answer as fully delivered (see <see cref="AnswerDelivered"/>).</summary>
        void MarkAnswerDelivered();
    }

    /// <summary>Scoped. Thread safe: parallel tool calls of the same turn may record at the same time.</summary>
    public sealed class ChatTurnFailureState : IChatTurnFailureState
    {
        private Entry _entry;
        private int _answerDelivered;

        public bool HasFailure => Volatile.Read(ref _entry) != null;

        public ChatTurnFailure Failure => Volatile.Read(ref _entry)?.Failure;

        public Exception Exception => Volatile.Read(ref _entry)?.Exception;

        public bool Record(ChatTurnFailure failure, Exception exception = null)
        {
            if (failure == null)
                return false;

            var candidate = new Entry(failure, exception);
            while (true)
            {
                var current = Volatile.Read(ref _entry);

                // A terminal failure is final; a warning never replaces anything.
                if (current != null && (current.Failure.IsTerminal || !failure.IsTerminal))
                    return false;

                if (ReferenceEquals(Interlocked.CompareExchange(ref _entry, candidate, current), current))
                    return true;
            }
        }

        public bool Record(string code, int? agentId = null, string agentName = null, string chatbotIconUrl = null, string detailCode = null, Exception exception = null) =>
            Record(ChatTurnFailure.Create(code, agentId, agentName, chatbotIconUrl, detailCode), exception);

        public bool AnswerDelivered => Volatile.Read(ref _answerDelivered) == 1;

        public void MarkAnswerDelivered() => Interlocked.Exchange(ref _answerDelivered, 1);

        private sealed record Entry(ChatTurnFailure Failure, Exception Exception);
    }
}
