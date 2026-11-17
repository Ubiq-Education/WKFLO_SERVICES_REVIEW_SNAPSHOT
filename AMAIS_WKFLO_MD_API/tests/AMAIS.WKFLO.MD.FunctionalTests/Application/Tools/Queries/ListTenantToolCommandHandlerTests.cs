using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.CreateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Queries.ListTenantTools;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tools.Queries
{
    public class ListTenantToolCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task ListTenantTool_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantToolData();
            Fixture.AddTenantContext("Test");
            var mediator = Fixture.GetMediator();
            var command = ListCreateCommandTenantTool();
            var successArrangeResult = (SuccessResult<TenantToolCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetListCommandTenantTool();

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<List<TenantToolGetResult>>>(result);
            var successResult = ((SuccessResult<List<TenantToolGetResult>>)result).Data;

            Assert.IsType<int>(successResult[0].Id);
            Assert.NotEqual(default, successResult[0].Id);
        }

        private static CreateTenantToolCommand ListCreateCommandTenantTool()
        {
            return new CreateTenantToolCommand
            {
                Name = "Test Name",
                Description = "Test Description",
                Type = "Test Type",
                Config = "Test Config",
                IsActive = true
            };
        }

        private static ListTenantToolsQuery GetListCommandTenantTool()
        {
            return new ListTenantToolsQuery
            {
            };
        }
    }
}
