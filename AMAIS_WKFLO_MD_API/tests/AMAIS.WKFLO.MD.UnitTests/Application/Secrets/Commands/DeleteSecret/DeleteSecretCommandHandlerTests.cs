using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.DeleteSecret
{
    public class DeleteSecretCommandHandlerTests : IClassFixture<DeleteSecretCommandHandlerTesting>
    {
        private readonly DeleteSecretCommandHandlerTesting _testing;

        public DeleteSecretCommandHandlerTests(DeleteSecretCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Fact]
        public async Task TestHandlerOk()
        {
            // Act
            var result = await _testing.DeleteSecretCommandHandler.Handle(new DeleteSecretCommand()
            {
                Id = _testing.SecretId
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.Ok);
            if (result is SuccessResult<SecretDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
            else
            {
                Assert.Fail("Error on TestHandlerOk");
            }
        }

        [Fact]
        public async Task TestHandlerNotFound()
        {
            // Act
            var result = await _testing.DeleteSecretCommandHandler.Handle(new DeleteSecretCommand()
            {
                Id = 0
            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == ResultType.NotFound);
            if (result is SuccessResult<SecretDeleteResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
