using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.Application.Secrets.Commands
{
    public class DeleteSecretCommandHandlerTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public DeleteSecretCommandHandlerTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task DeleteSecret_ResultOk()
        {
            // Arrange
            _fixture.DeleteSecretData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetCreateCommandSecret();
            var successArrangeResult = (SuccessResult<SecretCreateResult>)await mediator.Send(command, TestContext.Current.CancellationToken);
            var command2 = GetDeleteCommandSecret(successArrangeResult.Data.Id);

            // Act
            var result = (Result)await mediator.Send(command2, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<SuccessResult<SecretDeleteResult>>(result);
            var successResult = ((SuccessResult<SecretDeleteResult>)result).Data;

            Assert.IsType<int>(successResult.Id);
            Assert.NotEqual(default, successResult.Id);
        }

        [Fact]
        public async Task DeleteSecret_ResultInvalid_SecretMustExist()
        {
            // Arrange
            _fixture.DeleteSecretData();
            _fixture.AddTenantContext(null);
            var mediator = _fixture.GetMediator();
            var command = GetDeleteCommandSecret(-1);

            // Act
            var result = (Result)await mediator.Send(command, TestContext.Current.CancellationToken);

            // Assert
            Assert.IsType<InvalidResult>(result);
            var invalidResult = (InvalidResult)result;

            Assert.NotEmpty(invalidResult.Errors);
            Assert.Contains(invalidResult.Errors, m => m.Code == ErrorConst.SecretMustExist && m.Property == nameof(Secret));
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

        private static DeleteSecretCommand GetDeleteCommandSecret(int id)
        {
            return new DeleteSecretCommand
            {
                Id = id
            };
        }
    }
}
