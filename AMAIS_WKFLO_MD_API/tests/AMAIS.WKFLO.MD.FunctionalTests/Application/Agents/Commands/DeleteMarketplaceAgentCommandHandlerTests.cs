using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class DeleteMarketplaceAgentCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteMarketplaceAgentCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteMarketplaceAgent_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandMarketplaceAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentDeleteResult>>(result);
            var successResult = ((SuccessResult<MarketplaceAgentDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteMarketplaceAgent_AgentSharedByAnotherTenant_ResultOk_MarketplaceIsNotTenantScoped()
        {
            // Arrange — WI 7312 fix: this endpoint lives under EndPoints/Marketplace/Agents/, the
            // internal AMAIS-staff "Manage Marketplace Agents" tool, which must be able to manage
            // every marketplace agent across every tenant. Create the agent under "Test", then
            // switch the caller's tenant context to a DIFFERENT tenant ("Test2") before deleting —
            // this previously incorrectly 400'd with AgentMustExist.
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            _fixture.AddTenantContext("Test2");
            var command2 = GetDeleteCommandMarketplaceAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentDeleteResult>>(result);
        }

        [Fact]
        public async Task DeleteMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandMarketplaceAgent(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
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

        private static DeleteMarketplaceAgentCommand GetDeleteCommandMarketplaceAgent(int id)
        {
            return new DeleteMarketplaceAgentCommand
            {
                Id = id
            };
        }

        private static DeleteMarketplaceAgentCommand GetDeleteCommandNotExistingMarketplaceAgent()
        {
            return new DeleteMarketplaceAgentCommand
            {
                Id = 0
            };
        }
    }
}
