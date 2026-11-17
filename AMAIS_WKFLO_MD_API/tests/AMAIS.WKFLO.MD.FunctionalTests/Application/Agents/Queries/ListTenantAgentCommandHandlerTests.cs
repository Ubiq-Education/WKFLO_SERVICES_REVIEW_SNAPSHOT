using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Agents.Commands.CreateTenantAgent;
using AMAIS.WKFLO.MD.Application.Agents.Models;
using AMAIS.WKFLO.MD.Application.Agents.Queries.ListTenantAgents;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Agents.Queries
{
    public class ListTenantAgentCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task ListTenantAgent_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantAgentData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = ListCreateCommandTenantAgent();
            var successArrangeResult = (SuccessResult<TenantAgentCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetListCommandTenantAgent();

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<List<TenantAgentGetResult>>>(result);
            var successResult = ((SuccessResult<List<TenantAgentGetResult>>)result).Data;

            Assert.IsType<int>(successResult[0].Id);
            Assert.NotEqual(default, successResult[0].Id);
        }

        private static CreateTenantAgentCommand ListCreateCommandTenantAgent()
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

        private static ListTenantAgentsQuery GetListCommandTenantAgent()
        {
            return new ListTenantAgentsQuery
            {
            };
        }
    }
}
