using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Queries
{
    public class GetMarketplaceAgentCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task GetMarketplaceAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetGetCommandMarketplaceAgent(successArrangeResult.Data.Id);

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentGetResult>>(result);
            var successResult = ((SuccessResult<MarketplaceAgentGetResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task GetMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            Fixture.DeleteMarketplaceAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = GetQueryNotExistingMarketplaceAgent();

            // Act
            var result = await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        private static CreateMarketplaceAgentCommand GetCreateCommandMarketplaceAgent()
        {
            return new CreateMarketplaceAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt"
            };
        }

        private static GetMarketplaceAgentByIdQuery GetGetCommandMarketplaceAgent(int id)
        {
            return new GetMarketplaceAgentByIdQuery
            {
                Id = id
            };
        }

        private static GetMarketplaceAgentByIdQuery GetQueryNotExistingMarketplaceAgent()
        {
            return new GetMarketplaceAgentByIdQuery
            {
                Id = 0
            };
        }
    }
}
