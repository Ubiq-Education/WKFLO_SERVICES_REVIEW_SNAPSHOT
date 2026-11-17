using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.Application.Secrets.Queries.ListSecrets;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Secrets.Queries
{
    public class ListSecretCommandHandlerTests(TestFixture fixture) : IClassFixture<TestFixture>
    {
        private TestFixture Fixture => fixture;

        [Fact]
        public async Task ListSecret_ResultOk()
        {
            // Arrange
            Fixture.DeleteSecretData();
            Fixture.AddTenantContext(null);
            var mediator = Fixture.GetMediator();
            var command = ListCreateCommandSecret();
            var successArrangeResult = (SuccessResult<SecretCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetListCommandSecret();

            // Act
            var result = await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<List<SecretGetResult>>>(result);
            var successResult = ((SuccessResult<List<SecretGetResult>>)result).Data;

            Assert.IsType<int>(successResult[0].Id);
            Assert.NotEqual(default, successResult[0].Id);
        }

        private static CreateSecretCommand ListCreateCommandSecret()
        {
            return new CreateSecretCommand
            {
                ApiKey = "Test ApiKey",
                Name = "Test Endpoint",
                IsActive = true
            };
        }

        private static ListSecretsQuery GetListCommandSecret()
        {
            return new ListSecretsQuery
            {
            };
        }
    }
}
