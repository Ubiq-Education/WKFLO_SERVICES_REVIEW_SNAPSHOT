using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Secrets.Commands
{
    public class CreateSecretCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task CreateFirstSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandFirstSecret();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<SecretCreateResult>>(result);
            var successResult = ((SuccessResult<SecretCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        [Fact]
        public async Task CreateSecondSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecondSecret();

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<SecretCreateResult>>(result);
            var successResult = ((SuccessResult<SecretCreateResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
            Assert.NotNull(successResult.ApiKey);
            Assert.Equal(command.ApiKey, successResult.ApiKey);
            Assert.NotNull(successResult.Name);
            Assert.Equal(command.Name, successResult.Name);
            Assert.Equal(command.IsActive, successResult.IsActive);
        }

        private static CreateSecretCommand GetCreateCommandFirstSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }

        private static CreateSecretCommand GetCreateCommandSecondSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey 2",
                Name = "Test Endpoint 2",
                IsActive = false
            };
        }
    }
}
