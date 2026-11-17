using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateMarketplaceAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class UpdateMarketplaceAgentCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UpdateMarketplaceAgentCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpdateMarketplaceAgent_ResultOk()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandMarketplaceAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentUpdateResult>>(result);
            var successResult = ((SuccessResult<MarketplaceAgentUpdateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command2.Description, successResult.Description);
            Assert.NotNull(successResult.SystemPrompt);
            Assert.Equal(command2.SystemPrompt, successResult.SystemPrompt);
        }

        [Fact]
        public async Task UpdateMarketplaceAgent_AgentSharedByAnotherTenant_ResultOk_MarketplaceIsNotTenantScoped()
        {
            // Arrange — WI 7312 fix: this endpoint lives under EndPoints/Marketplace/Agents/, the
            // internal AMAIS-staff "Manage Marketplace Agents" tool, which must be able to manage
            // every marketplace agent across every tenant. Create the agent under "Test", then
            // switch the caller's tenant context to a DIFFERENT tenant ("Test2") before updating —
            // this previously incorrectly 400'd with AgentMustExist.
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandMarketplaceAgent();
            var successArrangeResult = (SuccessResult<MarketplaceAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);

            _fixture.AddTenantContext("Test2");
            var command2 = GetUpdateCommandMarketplaceAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<MarketplaceAgentUpdateResult>>(result);
        }

        [Fact]
        public async Task UpdateMarketplaceAgent_ResultInvalid_MarketplaceAgentMustExist()
        {
            // Arrange
            _fixture.DeleteMarketplaceAgentData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetUpdateCommandMarketplaceAgent(-1);

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

        private static UpdateMarketplaceAgentCommand GetUpdateCommandMarketplaceAgent(int id)
        {
            return new UpdateMarketplaceAgentCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2"
            };
        }

        private static UpdateMarketplaceAgentCommand GetUpdateCommandNotExistingMarketplaceAgent()
        {
            return new UpdateMarketplaceAgentCommand
            {
                Id = 0,
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt"
            };
        }
    }
}
