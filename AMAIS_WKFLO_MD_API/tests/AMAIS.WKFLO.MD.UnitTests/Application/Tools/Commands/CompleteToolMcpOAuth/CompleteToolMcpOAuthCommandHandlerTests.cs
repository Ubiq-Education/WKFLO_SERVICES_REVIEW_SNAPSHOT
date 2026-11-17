using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CompleteToolMcpOAuth;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Commands.CompleteToolMcpOAuth
{
    public class CompleteToolMcpOAuthCommandHandlerTests : IClassFixture<CompleteToolMcpOAuthCommandHandlerTesting>
    {
        private readonly CompleteToolMcpOAuthCommandHandlerTesting _testing;

        public CompleteToolMcpOAuthCommandHandlerTests(CompleteToolMcpOAuthCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            var result = await _testing.Handler.Handle(new CompleteToolMcpOAuthCommand
            {
                Code = "valid-code",
                State = CompleteToolMcpOAuthCommandHandlerTesting.PendingState
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<ToolMcpOAuthCompleteResult> success)
            {
                Assert.True(success.Data.Connected);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandler_MissingCode_ReturnsInvalid()
        {
            var result = await _testing.Handler.Handle(new CompleteToolMcpOAuthCommand
            {
                Code = string.Empty
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
        }

        [Fact]
        public async Task TestHandler_NoPendingFlow_ReturnsInvalid()
        {
            // Covers both "unrecognized/expired state" and the old separate "unknown connection id" case —
            // there's no connection id on the request anymore for the handler to look up on its own, so an
            // unmatched `state` is the only way this can fail short of IMcpOAuthConsentService itself.
            var result = await _testing.Handler.Handle(new CompleteToolMcpOAuthCommand
            {
                Code = "code-with-no-pending-flow"
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
        }

        [Fact]
        public async Task TestHandler_UnknownState_ReturnsInvalid()
        {
            // The route carries no id at all — an unrecognized `state` (forged, expired, or simply never
            // issued) is indistinguishable from "no pending flow" from this handler's point of view; both are
            // ConsentService.CompleteAsync returning false.
            var result = await _testing.Handler.Handle(new CompleteToolMcpOAuthCommand
            {
                Code = "valid-code",
                State = "some-other-state-nobody-was-given"
            }, TestContext.Current.CancellationToken);

            Assert.True(result.ResultType == ResultType.Invalid);
        }
    }
}
