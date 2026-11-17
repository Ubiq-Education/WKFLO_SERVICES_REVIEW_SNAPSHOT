using AMAIS.WKFLO.AI.Application.Common;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ChatErrors;
using System.Linq;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.Agents.ChatErrors
{
    /// <summary>US 7959: the failure model keeps codes inside their allowlists; the codes match the AI API ErrorConst.</summary>
    public class ChatTurnFailureTests
    {
        [Theory]
        [InlineData(ChatErrorCodes.ChatToolUnavailable)]
        [InlineData(ChatErrorCodes.ChatToolFailed)]
        public void Create_ToolCode_IsANonTerminalWarning(string code)
        {
            var failure = ChatTurnFailure.Create(code, agentId: 7, agentName: "Helper");

            Assert.Equal(code, failure.Code);
            Assert.Equal(ChatErrorSeverity.Warning, failure.Severity);
            Assert.False(failure.IsTerminal);
            Assert.Equal(7, failure.AgentId);
            Assert.Equal("Helper", failure.AgentName);
        }

        [Fact]
        public void Create_OtherCode_IsATerminalError()
        {
            var failure = ChatTurnFailure.Create(ChatErrorCodes.ChatModelTimeout);

            Assert.Equal(ChatErrorSeverity.Error, failure.Severity);
            Assert.True(failure.IsTerminal);
        }

        [Fact]
        public void Create_TerminalCanBeOverridden()
        {
            Assert.False(ChatTurnFailure.Create(ChatErrorCodes.ChatVoiceSynthesisFailed, isTerminal: false).IsTerminal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("WKFLOAI0001")]
        [InlineData("Object reference not set to an instance of an object.")]
        public void Create_UnknownCode_FallsBackToTurnFailed(string code)
        {
            Assert.Equal(ChatErrorCodes.ChatTurnFailed, ChatTurnFailure.Create(code).Code);
        }

        [Theory]
        [InlineData(ErrorConst.TenantInvalid)]
        [InlineData(ErrorConst.TenantMustExist)]
        [InlineData(ErrorConst.AgentMustExist)]
        [InlineData(ErrorConst.AgentNotFound)]
        [InlineData(ErrorConst.AgentNotRunnable)]
        public void Create_AllowlistedDetailCode_IsKept(string detailCode)
        {
            Assert.Equal(detailCode, ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable, detailCode: detailCode).DetailCode);
        }

        [Theory]
        [InlineData(ErrorConst.InvalidInput)]
        [InlineData("some internal text")]
        public void Create_OtherDetailCode_IsDropped(string detailCode)
        {
            Assert.Null(ChatTurnFailure.Create(ChatErrorCodes.ChatAgentUnavailable, detailCode: detailCode).DetailCode);
        }

        [Fact]
        public void ToPublicDetailCode_ReturnsTheFirstAllowlistedCode()
        {
            Assert.Equal(ErrorConst.AgentNotRunnable,
                ChatErrorCodes.ToPublicDetailCode([ErrorConst.InvalidInput, ErrorConst.AgentNotRunnable, ErrorConst.TenantInvalid]));
        }

        [Fact]
        public void PublicDetailCodes_MatchTheAiApiErrorConst()
        {
            string[] expected = [ErrorConst.TenantInvalid, ErrorConst.TenantMustExist, ErrorConst.AgentMustExist, ErrorConst.AgentNotFound, ErrorConst.AgentNotRunnable];

            Assert.Equal(expected.OrderBy(c => c), ChatErrorCodes.PublicDetailCodes.OrderBy(c => c));
        }

        [Fact]
        public void All_AreTheWkfloAi11xxCodes()
        {
            Assert.Equal(17, ChatErrorCodes.All.Count);
            Assert.All(ChatErrorCodes.All, c => Assert.Matches("^WKFLOAI11(0[1-9]|1[0-7])$", c));
        }
    }
}
