using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth;
using Moq;
using System.Threading;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.CompleteToolMcpOAuth
{
    public class CompleteToolMcpOAuthCommandHandlerTesting
    {
        public CompleteToolMcpOAuthCommandHandler Handler { get; }
        public Mock<IMcpOAuthConsentService> ConsentServiceMock { get; } = new();

        /// <summary>The `state` value a real pending flow was indexed under — see CompleteToolMcpOAuthCommand's
        /// remarks: with the callback route now a single static URL carrying no id, `state` is the only thing
        /// that correlates a callback request back to the right consent flow.</summary>
        public const string PendingState = "state-1";

        public CompleteToolMcpOAuthCommandHandlerTesting()
        {
            ConsentServiceMock
                .Setup(x => x.CompleteAsync("valid-code", PendingState, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            ConsentServiceMock
                .Setup(x => x.CompleteAsync("code-with-no-pending-flow", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Any other (code, state) combination — e.g. an unknown/forged state — isn't set up above, so the
            // loose mock's default (false) applies, matching real CompleteAsync's "no matching flow" behavior.

            Handler = new CompleteToolMcpOAuthCommandHandler(ConsentServiceMock.Object);
        }
    }
}
