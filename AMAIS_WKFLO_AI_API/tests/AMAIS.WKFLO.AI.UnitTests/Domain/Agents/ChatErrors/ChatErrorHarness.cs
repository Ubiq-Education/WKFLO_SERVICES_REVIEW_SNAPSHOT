using AMAIS.WKFLO.AI.Application.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatStop;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Dapr.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>
    /// US 7959 / Task 7963: the real notifier and agent-failure reporter over a recorded chat, with an in-memory dedupe
    /// store (first write wins, like the Dapr first-write claim) and a settable Stop.
    /// </summary>
    public sealed class ChatErrorHarness
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public ChatNotificationRecorder Chat { get; } = new();

        public Mock<IChatStopStateStore> StopState { get; } = new();

        public Mock<DaprClient> Dapr { get; } = new();

        public ChatTurnRegistry TurnRegistry { get; } = new();

        public ChatTurnErrorNotifier Notifier { get; }

        public ChatAgentFailureReporter Reporter { get; }

        public ChatErrorHarness()
        {
            Dapr.Setup(x => x.TrySaveStateAsync(CommonAiPubSubConsts.StateStoreName, It.IsAny<string>(), It.IsAny<ChatTurnErrorClaim>(),
                    It.IsAny<string>(), It.IsAny<StateOptions>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string key, ChatTurnErrorClaim _, string _, StateOptions _, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                {
                    lock (_claimed)
                        return _claimed.Add(key);
                });

            var clock = Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now);
            Notifier = new ChatTurnErrorNotifier(Chat.Object, StopState.Object, Dapr.Object, clock, NullLogger<ChatTurnErrorNotifier>.Instance);
            Reporter = new ChatAgentFailureReporter(Notifier, Chat.Object, TurnRegistry, NullLogger<ChatAgentFailureReporter>.Instance);
        }

        /// <summary>The user pressed Stop at <paramref name="stoppedAt"/>.</summary>
        public void StoppedAt(DateTimeOffset stoppedAt) =>
            StopState.Setup(x => x.GetStoppedAtAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)stoppedAt);
    }
}
