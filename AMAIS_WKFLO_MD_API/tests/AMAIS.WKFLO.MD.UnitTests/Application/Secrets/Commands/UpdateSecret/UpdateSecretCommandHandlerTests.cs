using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Secrets.Commands.UpdateSecret;
using AMAIS.WKFLO.MD.Application.Secrets.Models;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.UpdateSecret
{
    public class UpdateSecretCommandHandlerTests : IClassFixture<UpdateSecretCommandHandlerTesting>
    {
        private readonly UpdateSecretCommandHandlerTesting _testing;

        public UpdateSecretCommandHandlerTests(UpdateSecretCommandHandlerTesting testing)
        {
            _testing = testing;
        }

        [Theory]
        [InlineData("ApiKey 1", "Endpoint 1", true, ResultType.Ok)]
        [InlineData("ApiKey 2", "Endpoint 2", false, ResultType.Ok)]
        public async Task TestHandler(string apiKey, string endpoint, bool isActive, ResultType resultType)
        {
            // Act
            var result = await _testing.UpdateSecretCommandHandler.Handle(new UpdateSecretCommand()
            {
                Id = _testing.SecretId,
                ApiKey = apiKey,
                Name = endpoint,
                IsActive = isActive

            }, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.ResultType == resultType);
            if (result is SuccessResult<SecretUpdateResult> success)
            {
                Assert.NotNull(success.Data);
            }
        }
    }
}
