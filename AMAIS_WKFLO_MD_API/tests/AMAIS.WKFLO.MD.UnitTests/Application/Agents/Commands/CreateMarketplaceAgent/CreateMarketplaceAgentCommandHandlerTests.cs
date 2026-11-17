using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Commands.CreateMarketplaceAgent
{
    public class CreateMarketplaceAgentCommandHandlerTests : IClassFixture<CreateMarketplaceAgentCommandHandlerTesting>
    {
        private readonly CreateMarketplaceAgentCommandHandlerTesting _testing;

        public CreateMarketplaceAgentCommandHandlerTests(CreateMarketplaceAgentCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("Name Test 1", "Description Test 1", "SystemPrompt 1", ResultType.Ok)]
        [InlineData("Name Test 2", "Description Test 2", "SystemPrompt 2", ResultType.Ok)]
        public async Task TestHandler(string name, string description, string systemPrompt, ResultType resultType)
        {
            // Act
            var result = await _testing.CreateMarketplaceAgentCommandHandler.Handle(new CreateMarketplaceAgentCommand()
            {
                Name = name,
                Description = description,
                SystemPrompt = systemPrompt

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<MarketplaceAgentCreateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
