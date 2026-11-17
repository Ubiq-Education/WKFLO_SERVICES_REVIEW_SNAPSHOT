using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.DeleteTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Commands.PublishTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Commands
{
    public class DeleteTenantAgentCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteTenantAgentCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteTenantAgent_ResultOk()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandTenantAgent();
            var successArrangeResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandTenantAgent(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentDeleteResult>>(result);
            var successResult = ((SuccessResult<TenantAgentDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteTenantAgent_ResultInvalid_TenantAgentMustExist()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandTenantAgent(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
        }

        [Fact]
        public async Task DeleteTenantAgent_ResultInvalid_ThereIsNoTenant()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandNotExistingTenantAgent();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.AgentMustExist && m.Property == nameof(Agent));
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.TenantInvalid && m.Property == nameof(TenantContext.TenantName));
        }

        [Fact]
        public async Task DeleteTenantAgent_CascadesToPublishedAgent()
        {
            // Arrange
            _fixture.DeleteTenantAgentData();
            _fixture.AddTenantContext("Test");
            var mediator = _fixture.GetMediator();
            var db = _fixture.GetDbContext();

            var createCommand = GetCreateCommandTenantAgent();
            var createResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(createCommand, TestContext.Current.CancellationToken);

            var publishResult = (SuccessResult<TenantAgentPublishResult>)await mediator.Send(
                new PublishTenantAgentCommand { Id = createResult.Data.Id }, TestContext.Current.CancellationToken);

            var command = GetDeleteCommandTenantAgent(createResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<TenantAgentDeleteResult>>(result);

            // The linked PublishedTenantAgent (the entity the chat widget actually serves from) must also
            // be soft-deleted. EntityTag liveness is handled separately at read time by
            // ListEntitiesByTagQueryHandler — this handler deliberately does not touch EntityTag rows.
            var publishedAgentRow = await db.PublishedTenantAgents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == publishResult.Data.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(publishedAgentRow);
            Assert.True(publishedAgentRow.IsDeleted);
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

        private static DeleteTenantAgentCommand GetDeleteCommandTenantAgent(int id)
        {
            return new DeleteTenantAgentCommand
            {
                Id = id
            };
        }

        private static DeleteTenantAgentCommand GetDeleteCommandNotExistingTenantAgent()
        {
            return new DeleteTenantAgentCommand
            {
                Id = 0
            };
        }
    }
}
