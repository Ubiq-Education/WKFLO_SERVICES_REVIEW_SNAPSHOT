using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.UpdateMarketplaceAgent
{
    public class UpdateMarketplaceAgentCommandHandlerTests : IClassFixture<UpdateMarketplaceAgentCommandHandlerTesting>
    {
        private readonly UpdateMarketplaceAgentCommandHandlerTesting _testing;

        public UpdateMarketplaceAgentCommandHandlerTests(UpdateMarketplaceAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "SystemPrompt 1", ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "SystemPrompt 2", ResultType.Ok)]
        public async Task TestHandler(string name, string description, string systemPrompt, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateMarketplaceAgentCommandHandler.Handle(new UpdateMarketplaceAgentCommand()
            {
                Id = _testing.MarketplaceAgentId,
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<MarketplaceAgentUpdateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }

        [Fact]
        public async Task TestHandler_UpdatesIconUrlAndAIModelId()
        {
            // Act
            var result = await _testing.UpdateMarketplaceAgentCommandHandler.Handle(new UpdateMarketplaceAgentCommand()
            {
                Id = _testing.MarketplaceAgentId,
                Name = "Name",
                Description = "Description",
                SystemPrompt = "SystemPrompt",
                IconUrl = "https://static.example.com/updated-icon.png",
                AIModelId = 42
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentUpdateResult>>(result);
            Assert.Equal("https://static.example.com/updated-icon.png", success.Data.IconUrl);
            Assert.Equal(42, success.Data.AIModelId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestHandler_UpdatesEnableSTM(bool enableSTM)
        {
            // Act
            var result = await _testing.UpdateMarketplaceAgentCommandHandler.Handle(new UpdateMarketplaceAgentCommand()
            {
                Id = _testing.MarketplaceAgentId,
                Name = "Name",
                Description = "Description",
                SystemPrompt = "SystemPrompt",
                EnableSTM = enableSTM
            }, TestContext.Current.CancellationToken);

            // Assert
            var success = Assert.IsType<SuccessResult<MarketplaceAgentUpdateResult>>(result);
            Assert.Equal(enableSTM, success.Data.EnableSTM);
        }
    }
}
