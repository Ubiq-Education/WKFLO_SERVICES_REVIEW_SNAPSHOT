using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Secrets.Queries
{
    public class GetSecretCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task GetSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetCreateCommandSecret();
            var successArrangeResult = (SuccessResult<SecretCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetGetCommandSecret(successArrangeResult.Data.Id);

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<SecretGetResult>>(result);
            var successResult = ((SuccessResult<SecretGetResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task GetSecret_ResultInvalid_SecretMustExist()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = GetQueryNotExistingSecret();

            // Act
            var result = await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        private static CreateSecretCommand GetCreateCommandSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }

        private static GetSecretByIdQuery GetGetCommandSecret(int id)
        {
            return new GetSecretByIdQuery
            {
                Id = id
            };
        }

        private static GetSecretByIdQuery GetQueryNotExistingSecret()
        {
            return new GetSecretByIdQuery
            {
                Id = 0
            };
        }
    }
}
