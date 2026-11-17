using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class UpdateTenantAgentCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UpdateTenantAgentCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task UpdateTenantAgent_ResultOk()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandTenantAgent();
            var successArrangeResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetUpdateCommandTenantAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentUpdateResult>>(result);
            var successResult = ((SuccessResult<TenantAgentUpdateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command2.Name, successResult.Name);
            Assert.NotNull(successResult.Description);
            Assert.Equal(command2.Description, successResult.Description);
            Assert.NotNull(successResult.SystemPrompt);
            Assert.Equal(command2.SystemPrompt, successResult.SystemPrompt);
            Assert.Equal(command2.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task UpdateTenantAgent_ResultInvalid_TenantAgentMustExist()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetUpdateCommandTenantAgent(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
        }

        [Fact]
        public async Task UpdateTenantAgent_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command1 = GetUpdateCommandNotExistingTenantAgent();

            // Act
            var result = (Result)await mediator.Send(command1, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
        }


        private static CreateTenantAgentCommand GetCreateCommandTenantAgent()
        {
            return new CreateTenantAgentCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true
            };
        }

        private static UpdateTenantAgentCommand GetUpdateCommandTenantAgent(int id)
        {
            return new UpdateTenantAgentCommand
            {
                Id = id,
                Name = "Test Name 2",
                Description = "Test Description 2",
                SystemPrompt = "Test System Prompt 2",
                ResponseTone = "Friendly 2",
                IsActive = false
            };
        }

        private static UpdateTenantAgentCommand GetUpdateCommandNotExistingTenantAgent()
        {
            return new UpdateTenantAgentCommand
            {
                Id = 0,
                Name = "Test Name",
                Description = "Test Description",
                SystemPrompt = "Test System Prompt",
                ResponseTone = "Friendly",
                IsActive = true
            };
        }
    }
}
