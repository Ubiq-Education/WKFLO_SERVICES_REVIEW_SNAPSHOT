using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tenants.Commands.CreateTenant;
using AMAIS.WKFLO.MD.Application.Tenants.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Tenants.Commands
{
    public class CreateTenantCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task CreateTenant_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantData();

            var command = new CreateTenantCommand
            {
                Name = "NewTenant",
                Region = "EU"
            };

            // Act
            var result = await Fixture.GetMediator().Send(command, TestContext.Current.CancellationToken);

            // Assert
            result.ResultType.Should().Be(ResultType.Ok);
            var successResult = result as SuccessResult<TenantCreateResult>;
            successResult.Should().NotBeNull();
            successResult!.Data.Should().NotBeNull();
            successResult.Data.Name.Should().Be(command.Name);
            successResult.Data.Region.Should().Be(command.Region);
            successResult.Data.Id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task CreateMultipleTenants_ResultOk()
        {
            // Arrange
            Fixture.DeleteTenantData();

            var command1 = new CreateTenantCommand
            {
                Name = "Tenant1",
                Region = "UK"
            };

            var command2 = new CreateTenantCommand
            {
                Name = "Tenant2",
                Region = "US"
            };

            // Act
            var result1 = await Fixture.GetMediator().Send(command1, TestContext.Current.CancellationToken);
            var result2 = await Fixture.GetMediator().Send(command2, TestContext.Current.CancellationToken);

            // Assert
            result1.ResultType.Should().Be(ResultType.Ok);
            result2.ResultType.Should().Be(ResultType.Ok);

            var successResult1 = result1 as SuccessResult<TenantCreateResult>;
            var successResult2 = result2 as SuccessResult<TenantCreateResult>;

            successResult1.Should().NotBeNull();
            successResult2.Should().NotBeNull();

            successResult1!.Data.Name.Should().Be(command1.Name);
            successResult2!.Data.Name.Should().Be(command2.Name);

            successResult1.Data.Id.Should().NotBe(successResult2.Data.Id);
        }
    }
}

